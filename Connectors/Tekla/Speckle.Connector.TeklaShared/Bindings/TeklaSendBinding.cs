using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.TeklaShared.Operations.Send.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.TeklaShared;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;
using Tekla.Structures;
using Tekla.Structures.Model;
using Task = System.Threading.Tasks.Task;

namespace Speckle.Connectors.TeklaShared.Bindings;

public sealed class TeklaSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IServiceProvider _serviceProvider;
  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<TeklaSendBinding> _logger;
  private readonly ITeklaConversionSettingsFactory _teklaConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly Model _model;
  private readonly ToSpeckleSettingsManager _toSpeckleSettingsManager;
  private readonly Events _events;

  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  public TeklaSendBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    ICancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<TeklaSendBinding> logger,
    ITeklaConversionSettingsFactory teklaConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory,
    ToSpeckleSettingsManager toSpeckleSettingsManager
  )
  {
    _store = store;
    _serviceProvider = serviceProvider;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _teklaConversionSettingsFactory = teklaConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _activityFactory = activityFactory;
    _toSpeckleSettingsManager = toSpeckleSettingsManager;

    _model = new Model();
    _events = new Events();
    SubscribeToTeklaEvents();
  }

  private void SubscribeToTeklaEvents()
  {
    _events.ModelObjectChanged += OnModelObjectChanged;
    _events.Register();
  }

  // subscribes the all changes in a modelobject
  private void OnModelObjectChanged(List<ChangeData> changes)
  {
    foreach (var change in changes)
    {
      if (change.Object is { } modelObj)
      {
        ChangedObjectIds[modelObj.Identifier.GUID.ToString()] = 1;
      }
    }

    if (changes.Count > 0)
    {
      // directly calling the RunExpirationChecks, not triggering the idle
      // TODO: Figure out how idleing works in Tekla
      Task.FromResult(RunExpirationChecks());
    }
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [new SendRebarsAsSolidSetting(false)];

  public async Task Send(string modelCardId)
  {
    using var activity = _activityFactory.Start();

    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        throw new InvalidOperationException("No publish model card was found.");
      }
      using var scope = _serviceProvider.CreateScope();
      scope
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<TeklaConversionSettings>>()
        .Initialize(
          _teklaConversionSettingsFactory.Create(_model, _toSpeckleSettingsManager.GetSendRebarsAsSolid(modelCard))
        );

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

      List<ModelObject> teklaObjects = modelCard
        .SendFilter.NotNull()
        .RefreshObjectIds()
        .Select(id => _model.SelectModelObject(new Identifier(new Guid(id))))
        .Where(obj => obj != null)
        .ToList();

      if (teklaObjects.Count == 0)
      {
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<ModelObject>>()
        .Execute(
          teklaObjects,
          modelCard.GetSendInfo(_speckleApplication.ApplicationAndVersion),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationItem.Token),
          cancellationItem.Token
        );

      await Commands.SetModelSendResult(modelCardId, sendResult.VersionId, sendResult.ConversionResults);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex);
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  private async Task RunExpirationChecks()
  {
    if (!_model.GetConnectionStatus())
    {
      _logger.LogError("Tekla expiration checks were running without an active model.");
      return;
    }

    var senders = _store.GetSenders();
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    List<string> expiredSenderIds = new();

    _sendConversionCache.EvictObjects(objectIdsList);

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().SelectedObjectIds.Intersect(objectIdsList).ToList();
      var isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds);

    ChangedObjectIds = new ConcurrentDictionary<string, byte>();
  }
}
