using System.IO;
using Autodesk.Navisworks.Api;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Operations.Send.Filters;
using Speckle.Connector.Navisworks.Operations.Send.Settings;
using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.Common.Cancellation;
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
using Speckle.Sdk.Pipelines.Progress;

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
      new NavisworksSavedSetsFilter(new ConnectorElementSelectionService()),
      new NavisworksSavedViewsFilter(new ConnectorElementSelectionService()),
    ];

  public List<ICardSetting> GetSendSettings() =>
    [
      new VisualRepresentationSetting(RepresentationMode.Active),
      new OriginModeSetting(OriginMode.ModelOrigin),
      new PropertyDetailLevelSetting(PropertyDetailLevel.Standard),
      new GeometryDetailLevelSetting(GeometryDetailLevel.Optimised),
      new ConvertHiddenElementsSetting(false),
      new PreserveModelHierarchySetting(true),
      new RevitCategoryMappingSetting(false),
    ];

  public async Task Send(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  private async Task SendInternal(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    var (fileName, fileSizeBytes) = GetFileInfo();
    await manager.Process(
      Commands,
      modelCardId,
      InitializeConverterSettings,
      GetNavisworksModelItems,
      fileName,
      fileSizeBytes
    );
  }

  private (string? fileName, long? fileSizeBytes) GetFileInfo()
  {
    Document? activeDoc = NavisworksApp.ActiveDocument;
    if (activeDoc is null || !File.Exists(activeDoc.FileName))
    {
      return (null, null);
    }

    FileInfo fileInfo = new(activeDoc.FileName);
    return (fileInfo.Name, fileInfo.Length);
  }

  private void InitializeConverterSettings(IServiceProvider serviceProvider, SenderModelCard modelCard) =>
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<NavisworksConversionSettings>>()
      .Initialize(
        _conversionSettingsFactory.Create(
          originMode: _toSpeckleSettingsManagerNavisworks.GetOriginMode(modelCard),
          visualRepresentationMode: _toSpeckleSettingsManagerNavisworks.GetVisualRepresentationMode(modelCard),
          propertyDetailLevel: _toSpeckleSettingsManagerNavisworks.GetPropertyDetailLevel(modelCard),
          geometryDetailLevel: _toSpeckleSettingsManagerNavisworks.GetGeometryDetailLevel(modelCard),
          convertHiddenElements: _toSpeckleSettingsManagerNavisworks.GetConvertHiddenElements(modelCard),
          includeInternalProperties: _toSpeckleSettingsManagerNavisworks.GetIncludeInternalProperties(modelCard),
          roundMeshVertexDoubles: _toSpeckleSettingsManagerNavisworks.GetRoundMeshVertexDoubles(modelCard),
          preserveModelHierarchy: _toSpeckleSettingsManagerNavisworks.GetPreserveModelHierarchy(modelCard),
          mappingToRevitCategories: _toSpeckleSettingsManagerNavisworks.GetMappingToRevitCategories(modelCard)
        )
      );

  private Task<IReadOnlyList<NAV.ModelItem>> GetNavisworksModelItems(
    SenderModelCard modelCard,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    const double PRE_CONVERSION_START = 0d;
    const double PRE_CONVERSION_END = 0.28d;
    const double TREE_TRAVERSAL_MAX = 0.26d;
    const int REPORT_INTERVAL = 1000;

    var selectedPaths = modelCard.SendFilter.NotNull().RefreshObjectIds();

    var convertHiddenElementsSetting =
      modelCard.Settings!.FirstOrDefault(s => s.Id == "convertHiddenElements")?.Value as bool? ?? false;
    var includeHiddenElements = convertHiddenElementsSetting;
    var message = convertHiddenElementsSetting
      ? "No visible objects were found to convert. Please update your publish filter!"
      : "No objects were found to convert. Please update your publish filter, or check items are visible!";

    var plannedSelection = BuildSelectionPlan(selectedPaths);
    if (plannedSelection.RootPaths.Count == 0)
    {
      throw new SpeckleSendFilterException(message);
    }

    onOperationProgressed.Report(new CardProgress("Getting selection...", PRE_CONVERSION_START));

    int estimatedCapacity = plannedSelection.RootPaths.Count * 10;
    var modelItems = new List<NAV.ModelItem>(estimatedCapacity);
    var seenGeometryItemGuids = new HashSet<Guid>();
    double count = 0;

    foreach (var path in plannedSelection.RootPaths)
    {
      double rootProgress = count / plannedSelection.RootPaths.Count;
      double baseProgress = PRE_CONVERSION_START + (PRE_CONVERSION_END - PRE_CONVERSION_START) * rootProgress;
      onOperationProgressed.Report(
        new CardProgress(
          $"Getting selection... ({plannedSelection.PrunedDescendantCount:N0} redundant paths pruned)",
          baseProgress
        )
      );

      var modelItem = _selectionService.GetModelItemFromPath(path);
      var hasChildren = modelItem.Children.Any();

      if (hasChildren)
      {
        int nodesVisited = 0;
        int hiddenBranchesPruned = 0;

        var traversalStack = new Stack<(NAV.ModelItem Node, bool AncestorsVisible)>();
        traversalStack.Push((modelItem, true));

        while (traversalStack.Count > 0)
        {
          var (node, ancestorsVisible) = traversalStack.Pop();
          nodesVisited++;

          if (nodesVisited % REPORT_INTERVAL == 0)
          {
            double visitedSignal = (double)nodesVisited / (nodesVisited + REPORT_INTERVAL);
            double treeProgress = PRE_CONVERSION_START + TREE_TRAVERSAL_MAX * rootProgress;
            double progress = Math.Min(
              PRE_CONVERSION_END,
              treeProgress + (TREE_TRAVERSAL_MAX / plannedSelection.RootPaths.Count) * visitedSignal
            );
            onOperationProgressed.Report(
              new CardProgress(
                $"Expanding tree: {nodesVisited} visited, {modelItems.Count} with geometry, {hiddenBranchesPruned} hidden",
                progress
              )
            );
          }

          bool isVisible = includeHiddenElements || (ancestorsVisible && !node.IsHidden);
          if (!isVisible)
          {
            hiddenBranchesPruned++;
            continue;
          }

          if (node.HasGeometry && seenGeometryItemGuids.Add(node.InstanceGuid))
          {
            modelItems.Add(node);
          }

          for (int i = node.Children.Count - 1; i >= 0; i--)
          {
            traversalStack.Push((node.Children[i], isVisible));
          }
        }
      }
      else
      {
        bool isVisible = includeHiddenElements || _selectionService.IsVisible(modelItem);
        if (modelItem.HasGeometry && isVisible && seenGeometryItemGuids.Add(modelItem.InstanceGuid))
        {
          modelItems.Add(modelItem);
        }
      }

      count++;
    }

    onOperationProgressed.Report(
      new CardProgress($"Selection resolved: {modelItems.Count:N0} geometry objects", PRE_CONVERSION_END)
    );

    if (modelItems.Count == 0)
    {
      throw new SpeckleSendFilterException(message);
    }

    return Task.FromResult<IReadOnlyList<NAV.ModelItem>>(modelItems);
  }

  private static SelectionPlan BuildSelectionPlan(IEnumerable<string> selectedPaths)
  {
    var cleanedDistinctPaths = selectedPaths
      .Where(path => !string.IsNullOrWhiteSpace(path))
      .Select(ElementSelectionHelper.GetCleanPath)
      .Distinct(StringComparer.Ordinal)
      .OrderBy(path => path.Count(c => c == '.'))
      .ThenBy(path => path, StringComparer.Ordinal)
      .ToList();

    if (cleanedDistinctPaths.Count == 0)
    {
      return new SelectionPlan([], 0);
    }

    var rootPaths = new List<string>(cleanedDistinctPaths.Count);
    var acceptedPaths = new HashSet<string>(StringComparer.Ordinal);
    int prunedDescendantCount = 0;

    foreach (var path in cleanedDistinctPaths)
    {
      if (HasSelectedAncestor(path, acceptedPaths))
      {
        prunedDescendantCount++;
        continue;
      }

      acceptedPaths.Add(path);
      rootPaths.Add(path);
    }

    return new SelectionPlan(rootPaths, prunedDescendantCount);
  }

  private static bool HasSelectedAncestor(string path, ISet<string> acceptedPaths)
  {
    int separatorIndex = path.LastIndexOf('.');
    while (separatorIndex > 0)
    {
      string ancestor = path[..separatorIndex];
      if (acceptedPaths.Contains(ancestor))
      {
        return true;
      }

      separatorIndex = ancestor.LastIndexOf('.');
    }

    return false;
  }

  private sealed record SelectionPlan(IReadOnlyList<string> RootPaths, int PrunedDescendantCount);

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public void CancelAllSendOperations()
  {
    foreach (var modelCardId in _store.GetSenders().Select(m => m.ModelCardId))
    {
      CancelSend(modelCardId ?? string.Empty);
    }
  }
}
