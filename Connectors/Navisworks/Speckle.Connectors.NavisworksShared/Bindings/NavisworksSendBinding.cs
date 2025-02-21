using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.Operations.Send.Settings;
using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }

  public SendBindingUICommands Commands { get; }

  private readonly DocumentModelStore _store;
  private readonly IServiceProvider _serviceProvider;
  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<NavisworksSendBinding> _logger;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly INavisworksConversionSettingsFactory _conversionSettingsFactory;
  private readonly ToSpeckleSettingsManagerNavisworks _toSpeckleSettingsManagerNavisworks;
  private readonly IElementSelectionService _selectionService;
  private readonly IThreadContext _threadContext;

  public NavisworksSendBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    ICancellationManager cancellationManager,
    IOperationProgressManager operationProgressManager,
    ILogger<NavisworksSendBinding> logger,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory,
    INavisworksConversionSettingsFactory conversionSettingsFactory,
    ToSpeckleSettingsManagerNavisworks toSpeckleSettingsManagerNavisworks,
    IElementSelectionService selectionService,
    IThreadContext threadContext
  )
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _store = store;
    _serviceProvider = serviceProvider;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _speckleApplication = speckleApplication;
    _activityFactory = activityFactory;
    _conversionSettingsFactory = conversionSettingsFactory;
    _toSpeckleSettingsManagerNavisworks = toSpeckleSettingsManagerNavisworks;
    _selectionService = selectionService;
    _threadContext = threadContext;
    SubscribeToNavisworksEvents();
  }

  private static void SubscribeToNavisworksEvents() { }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() =>
    [
      new VisualRepresentationSetting(RepresentationMode.Active),
      new OriginModeSetting(OriginMode.ModelOrigin),
      new IncludeInternalPropertiesSetting(false),
      new ConvertHiddenElementsSetting(false),
    ];

  public async Task Send(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  private async Task SendInternal(string modelCardId)
  {
    using var activity = _activityFactory.Start();
    try
    {
      var modelCard = GetModelCard(modelCardId);

      using var scope = _serviceProvider.CreateScope();

      InitializeConverterSettings(scope, modelCard);

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

      var navisworksModelItems = GetNavisworksModelItems(modelCard);

      var sendResult = await ExecuteSendOperation(scope, modelCard, navisworksModelItems, cancellationItem.Token);

      await Commands.SetModelSendResult(modelCardId, sendResult.RootObjId, sendResult.ConversionResults);
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex);
    }
  }

  private SenderModelCard GetModelCard(string modelCardId) =>
    _store.GetModelById(modelCardId) is not SenderModelCard modelCard
      ? throw new InvalidOperationException("No publish model card was found.")
      : modelCard;

  private void InitializeConverterSettings(IServiceScope scope, SenderModelCard modelCard) =>
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<NavisworksConversionSettings>>()
      .Initialize(
        _conversionSettingsFactory.Create(
          originMode: _toSpeckleSettingsManagerNavisworks.GetOriginMode(modelCard),
          visualRepresentationMode: _toSpeckleSettingsManagerNavisworks.GetVisualRepresentationMode(modelCard),
          convertHiddenElements: _toSpeckleSettingsManagerNavisworks.GetConvertHiddenElements(modelCard),
          includeInternalProperties: _toSpeckleSettingsManagerNavisworks.GetIncludeInternalProperties(modelCard)
        )
      );

  private List<NAV.ModelItem> GetNavisworksModelItems(SenderModelCard modelCard)
  {
    var selectedPaths = modelCard.SendFilter.NotNull().RefreshObjectIds();
    if (selectedPaths.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
    }

    var modelItems = modelCard
      .SendFilter.NotNull()
      .RefreshObjectIds()
      .Select(_selectionService.GetModelItemFromPath)
      .SelectMany(_selectionService.GetGeometryNodes)
      .Where(_selectionService.IsVisible)
      .ToList();

    return modelItems.Count == 0
      ? throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!")
      : modelItems;
  }

  private async Task<SendOperationResult> ExecuteSendOperation(
    IServiceScope scope,
    SenderModelCard modelCard,
    List<NAV.ModelItem> navisworksModelItems,
    CancellationToken token
  ) =>
    await scope
      .ServiceProvider.GetRequiredService<SendOperation<NAV.ModelItem>>()
      .Execute(
        navisworksModelItems,
        modelCard.GetSendInfo(_speckleApplication.ApplicationAndVersion),
        _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCard.ModelCardId.NotNull(), token),
        token
      );

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  /// <summary>
  /// Cancels all outstanding send operations for the current document.
  /// This method is called when the active document changes, to ensure
  /// that any in-progress send operations are properly canceled before
  /// the new document is loaded.
  /// </summary>
  public void CancelAllSendOperations()
  {
    foreach (var modelCardId in _store.GetSenders().Select(m => m.ModelCardId))
    {
      CancelSend(modelCardId ?? string.Empty);
    }
  }
}
