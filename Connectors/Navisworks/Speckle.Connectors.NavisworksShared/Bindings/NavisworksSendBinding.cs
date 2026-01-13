using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Operations.Send.Filters;
using Speckle.Connector.Navisworks.Operations.Send.Settings;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using ElementSelectionService = Speckle.Connector.Navisworks.Services.ElementSelectionService;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }

  public SendBindingUICommands Commands { get; }

  private readonly DocumentModelStore _store;
  private readonly ICancellationManager _cancellationManager;
  private readonly INavisworksConversionSettingsFactory _conversionSettingsFactory;
  private readonly ToSpeckleSettingsManagerNavisworks _toSpeckleSettingsManagerNavisworks;
  private readonly IElementSelectionService _selectionService;
  private readonly IThreadContext _threadContext;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;

  public NavisworksSendBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    ICancellationManager cancellationManager,
    INavisworksConversionSettingsFactory conversionSettingsFactory,
    ToSpeckleSettingsManagerNavisworks toSpeckleSettingsManagerNavisworks,
    IElementSelectionService selectionService,
    IThreadContext threadContext,
    ISendOperationManagerFactory sendOperationManagerFactory
  )
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _store = store;
    _cancellationManager = cancellationManager;
    _conversionSettingsFactory = conversionSettingsFactory;
    _toSpeckleSettingsManagerNavisworks = toSpeckleSettingsManagerNavisworks;
    _selectionService = selectionService;
    _threadContext = threadContext;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    SubscribeToNavisworksEvents();
  }

  private static void SubscribeToNavisworksEvents() { }

  // WARNING: Changes to filter behavior here must match everywhere filters are used, or saved sets won't update correctly
  public List<ISendFilter> GetSendFilters() =>
    [
      new NavisworksSelectionFilter() { IsDefault = true },
      new NavisworksSavedSetsFilter(new ElementSelectionService()),
      new NavisworksSavedViewsFilter(new ElementSelectionService())
    ];

  public List<ICardSetting> GetSendSettings() =>
    [
      new VisualRepresentationSetting(RepresentationMode.Active),
      new OriginModeSetting(OriginMode.ModelOrigin),
      new IncludeInternalPropertiesSetting(false),
      new ConvertHiddenElementsSetting(false),
      new PreserveModelHierarchySetting(false),
      new RevitCategoryMappingSetting(false)
    ];

  public async Task Send(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  private async Task SendInternal(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    await manager.Process(Commands, modelCardId, InitializeConverterSettings, GetNavisworksModelItems);
  }

  private void InitializeConverterSettings(IServiceProvider serviceProvider, SenderModelCard modelCard) =>
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<NavisworksConversionSettings>>()
      .Initialize(
        _conversionSettingsFactory.Create(
          originMode: _toSpeckleSettingsManagerNavisworks.GetOriginMode(modelCard),
          visualRepresentationMode: _toSpeckleSettingsManagerNavisworks.GetVisualRepresentationMode(modelCard),
          convertHiddenElements: _toSpeckleSettingsManagerNavisworks.GetConvertHiddenElements(modelCard),
          includeInternalProperties: _toSpeckleSettingsManagerNavisworks.GetIncludeInternalProperties(modelCard),
          preserveModelHierarchy: _toSpeckleSettingsManagerNavisworks.GetPreserveModelHierarchy(modelCard),
          mappingToRevitCategories: _toSpeckleSettingsManagerNavisworks.GetMappingToRevitCategories(modelCard)
        )
      );

  private async Task<IReadOnlyList<NAV.ModelItem>> GetNavisworksModelItems(
    SenderModelCard modelCard,
    IProgress<CardProgress> onOperationProgressed
  )
  {
#if DEBUG
    var methodEntryTime = Stopwatch.GetTimestamp();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] GetNavisworksModelItems ENTRY");
#endif

    var refreshTimer = Stopwatch.StartNew();
    var selectedPaths = modelCard.SendFilter.NotNull().RefreshObjectIds();
    refreshTimer.Stop();

#if DEBUG
    Console.WriteLine($"RefreshObjectIds took {refreshTimer.ElapsedMilliseconds}ms for {selectedPaths.Count} paths");
#endif
    var convertHiddenElementsSetting =
      modelCard.Settings!.FirstOrDefault(s => s.Id == "convertHiddenElements")?.Value as bool? ?? false;
    var message = convertHiddenElementsSetting
      ? "No visible objects were found to convert. Please update your publish filter!"
      : "No objects were found to convert. Please update your publish filter, or check items are visible!";

    if (selectedPaths.Count == 0)
    {
      throw new SpeckleSendFilterException(message);
    }

    onOperationProgressed.Report(new CardProgress("Getting selection...", null));
    await Task.CompletedTask;

    var totalStopwatch = Stopwatch.StartNew();
    long pathResolutionTicks = 0;
    long treeWalkTicks = 0;
    long visibilityCheckTicks = 0;
    long progressReportTicks = 0;
    long listOperationTicks = 0;
    int totalDescendantsProcessed = 0;
    var pathTimings = new List<(string path, long ticks, int descendants)>();
    int estimatedCapacity = selectedPaths.Count * 10;
    var modelItems = new List<NAV.ModelItem>(estimatedCapacity);
    double count = 0;
    foreach (var path in selectedPaths)
    {
      var progressTimer = Stopwatch.StartNew();
      onOperationProgressed.Report(new CardProgress("Getting selection...", count / selectedPaths.Count));
      await Task.CompletedTask;
      progressTimer.Stop();
      progressReportTicks += progressTimer.ElapsedTicks;

      var perPathTimer = Stopwatch.StartNew();
      var pathTimer = Stopwatch.StartNew();
      var modelItem = _selectionService.GetModelItemFromPath(path);
      pathTimer.Stop();
      pathResolutionTicks += pathTimer.ElapsedTicks;

      var hasChildren = modelItem.Children.Any();
      // ReSharper disable once UnusedVariable
      var isLeafGeometry = !hasChildren && modelItem.HasGeometry;

      var treeWalkTimer = Stopwatch.StartNew();
      var visibilityTimer = Stopwatch.StartNew();
      visibilityTimer.Stop();

      if (hasChildren)
      {
        int nodesVisited = 0;
        int hiddenBranchesPruned = 0;
        const int REPORT_INTERVAL = 1000;

        void TraverseWithProgress(NAV.ModelItem node)
        {
          nodesVisited++;

          if (nodesVisited % REPORT_INTERVAL == 0)
          {
            onOperationProgressed.Report(
              new CardProgress(
                $"Expanding tree: {nodesVisited} visited, {modelItems.Count} with geometry, {hiddenBranchesPruned} hidden",
                null
              )
            );
            Task.Delay(1).Wait();
          }

          visibilityTimer.Start();
          bool isVisible = _selectionService.IsVisible(node);
          visibilityTimer.Stop();

          if (!isVisible)
          {
            hiddenBranchesPruned++;
            return;
          }

          if (node.HasGeometry)
          {
            var listTimer = Stopwatch.StartNew();
            modelItems.Add(node);
            listTimer.Stop();
            listOperationTicks += listTimer.ElapsedTicks;
          }

          foreach (var child in node.Children)
          {
            TraverseWithProgress(child);
          }
        }

        TraverseWithProgress(modelItem);
        totalDescendantsProcessed = nodesVisited;
      }
      else
      {
        totalDescendantsProcessed = 1;
        visibilityTimer.Start();
        if (modelItem.HasGeometry && _selectionService.IsVisible(modelItem))
        {
          visibilityTimer.Stop();
          var listTimer = Stopwatch.StartNew();
          modelItems.Add(modelItem);
          listTimer.Stop();
          listOperationTicks += listTimer.ElapsedTicks;
        }
        else
        {
          visibilityTimer.Stop();
        }
      }

      treeWalkTimer.Stop();
      treeWalkTicks += treeWalkTimer.ElapsedTicks;
      visibilityCheckTicks += visibilityTimer.ElapsedTicks;

      perPathTimer.Stop();
      pathTimings.Add((path, perPathTimer.ElapsedTicks, totalDescendantsProcessed));
      count++;
    }

    totalStopwatch.Stop();

#if DEBUG
    var pathResolutionMs = pathResolutionTicks / (double)TimeSpan.TicksPerMillisecond;
    var treeWalkMs = treeWalkTicks / (double)TimeSpan.TicksPerMillisecond;
    var visibilityMs = visibilityCheckTicks / (double)TimeSpan.TicksPerMillisecond;
    var progressMs = progressReportTicks / (double)TimeSpan.TicksPerMillisecond;
    var listOpsMs = listOperationTicks / (double)TimeSpan.TicksPerMillisecond;
    var totalMs = totalStopwatch.ElapsedMilliseconds;

    var accountedMs = pathResolutionMs + treeWalkMs + visibilityMs + progressMs + listOpsMs;
    var unaccountedMs = totalMs - accountedMs;

    Console.WriteLine("=== GetNavisworksModelItems Performance ===");
    Console.WriteLine($"Selected Paths: {selectedPaths.Count}");
    Console.WriteLine($"Total Descendants Processed: {totalDescendantsProcessed}");
    Console.WriteLine($"Final Geometry Items: {modelItems.Count}");
    Console.WriteLine($"Total Time: {totalMs}ms");
    Console.WriteLine($"  Path Resolution: {pathResolutionMs:F2}ms ({pathResolutionMs / totalMs * 100:F1}%)");
    Console.WriteLine($"  Tree Walking: {treeWalkMs:F2}ms ({treeWalkMs / totalMs * 100:F1}%)");
    Console.WriteLine($"  Visibility Checks: {visibilityMs:F2}ms ({visibilityMs / totalMs * 100:F1}%)");
    Console.WriteLine($"  Progress Reporting: {progressMs:F2}ms ({progressMs / totalMs * 100:F1}%)");
    Console.WriteLine($"  List Operations: {listOpsMs:F2}ms ({listOpsMs / totalMs * 100:F1}%)");
    Console.WriteLine($"  Unaccounted Overhead: {unaccountedMs:F2}ms ({unaccountedMs / totalMs * 100:F1}%)");
    Console.WriteLine($"Avg per path: {totalMs / selectedPaths.Count:F2}ms");
    Console.WriteLine($"Avg descendants per path: {totalDescendantsProcessed / selectedPaths.Count:F0}");

    // Show the slowest paths (top 5)
    var slowestPaths = pathTimings.OrderByDescending(x => x.ticks).Take(5).ToList();
    if (slowestPaths.Count != 0)
    {
      Console.WriteLine("\nSlowest 5 paths:");
      for (int i = 0; i < slowestPaths.Count; i++)
      {
        (string path, long ticks, int descendants) = slowestPaths[i];
        var ms = ticks / (double)TimeSpan.TicksPerMillisecond;
        Console.WriteLine(
          $"  {i + 1}. {ms:F2}ms - {descendants} descendants - Path: {(path.Length > 50 ? path[..50] + "..." : path)}"
        );
      }
    }

    var methodTotalMs = (Stopwatch.GetTimestamp() - methodEntryTime) / (double)TimeSpan.TicksPerMillisecond;
    Console.WriteLine(
      $"\n[{DateTime.Now:HH:mm:ss.fff}] GetNavisworksModelItems EXIT - Total method time: {methodTotalMs:F2}ms"
    );
#endif

    return modelItems.Count == 0 ? throw new SpeckleSendFilterException(message) : modelItems;
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public void CancelAllSendOperations()
  {
    foreach (var modelCardId in _store.GetSenders().Select(m => m.ModelCardId))
    {
      CancelSend(modelCardId ?? string.Empty);
    }
  }
}
