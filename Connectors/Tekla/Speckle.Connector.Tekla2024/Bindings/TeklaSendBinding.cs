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
using Speckle.Converter.Tekla2024;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;
using Tekla.Structures;
using Tekla.Structures.Model;
using Task = System.Threading.Tasks.Task;

namespace Speckle.Connector.Tekla2024.Bindings;

public sealed class TeklaSendBinding : ISendBinding, IDisposable
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IAppIdleManager _idleManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly List<ISendFilter> _sendFilters;
  private readonly CancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<TeklaSendBinding> _logger;
  private readonly ITeklaConversionSettingsFactory _teklaConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly Model _model;
  private readonly Events _events;

  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  public TeklaSendBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    CancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<TeklaSendBinding> logger,
    ITeklaConversionSettingsFactory teklaConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory
  )
  {
    _store = store;
    _idleManager = idleManager;
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

    _model = new Model();
    _events = new Events();
    SubscribeToTeklaEvents();
  }

  private void SubscribeToTeklaEvents()
  {
    _events.ModelObjectChanged += ModelHandler_OnChange;
    _events.Register();
  }

  private void ModelHandler_OnChange(List<ChangeData> changes)
  {
    foreach (var change in changes)
    {
      if (change.Object is ModelObject modelObj)
      {
        ChangedObjectIds[modelObj.Identifier.ID.ToString()] = 1;
      }
    }

    if (changes.Count > 0)
    {
      _idleManager.SubscribeToIdle(nameof(TeklaSendBinding), () => RunExpirationChecks());
    }
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    using var activity = _activityFactory.Start();
    using var scope = _serviceProvider.CreateScope();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<TeklaConversionSettings>>()
      .Initialize(_teklaConversionSettingsFactory.Create(_model));

    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        throw new InvalidOperationException("No publish model card was found.");
      }

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      List<ModelObject> teklaObjects = modelCard
        .SendFilter.NotNull()
        .GetObjectIds()
        .Select(id => _model.SelectModelObject(new Identifier(Convert.ToInt32(id))))
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
          modelCard.GetSendInfo(_speckleApplication.Slug),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationToken),
          cancellationToken
        )
        .ConfigureAwait(false);

      await Commands
        .SetModelSendResult(modelCardId, sendResult.RootObjId, sendResult.ConversionResults)
        .ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex).ConfigureAwait(false);
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
      var intersection = modelCard.SendFilter.NotNull().GetObjectIds().Intersect(objectIdsList).ToList();
      var isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds).ConfigureAwait(false);
    ChangedObjectIds = new();
  }

  private bool _disposed;

  public void Dispose()
  {
    if (!_disposed)
    {
      if (_events != null)
      {
        _events.UnRegister();
      }
      _disposed = true;
    }
  }
}
