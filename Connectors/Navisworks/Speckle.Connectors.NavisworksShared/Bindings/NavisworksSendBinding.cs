using System.IO;
using Autodesk.Navisworks.Api;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Operations.Send.Filters;
using Speckle.Connector.Navisworks.Operations.Send.Settings;
using Speckle.Connector.Navisworks.Services;
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
      new NavisworksSavedViewsFilter(new ConnectorElementSelectionService())
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
    var selectedPaths = modelCard.SendFilter.NotNull().RefreshObjectIds();

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

    int estimatedCapacity = selectedPaths.Count * 10;
    var modelItems = new List<NAV.ModelItem>(estimatedCapacity);
    double count = 0;

    foreach (var path in selectedPaths)
    {
      onOperationProgressed.Report(new CardProgress("Getting selection...", count / selectedPaths.Count));
      await Task.CompletedTask;

      var modelItem = _selectionService.GetModelItemFromPath(path);
      var hasChildren = modelItem.Children.Any();

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

          if (!_selectionService.IsVisible(node))
          {
            hiddenBranchesPruned++;
            return;
          }

          if (node.HasGeometry)
          {
            modelItems.Add(node);
          }

          foreach (var child in node.Children)
          {
            TraverseWithProgress(child);
          }
        }

        TraverseWithProgress(modelItem);
      }
      else
      {
        if (modelItem.HasGeometry && _selectionService.IsVisible(modelItem))
        {
          modelItems.Add(modelItem);
        }
      }

      count++;
    }

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
