using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.CSiShared.Bindings;

public sealed class CSiSharedSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IAppIdleManager _idleManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly List<ISendFilter> _sendFilters;
  private readonly CancellationManager _cancellationManager;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<CSiSharedSendBinding> _logger;
  private readonly ICSiConversionSettingsFactors _csiConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;

  public CSiSharedSendBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    CancellationManager cancellationManager,
    IOperationProgressManager operationProgressManager,
    ILogger<CSiSharedSendBinding> logger,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory
  )
  {
    _store = store;
    _idleManager = idleManager;
    _serviceProvider = serviceProvider;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _speckleApplication = speckleApplication;
    _activityFactory = activityFactory;
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    using var activity = _activityFactory.Start();
    using var scope = _serviceProvider.CreateScope();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<CSiConversionSettings>>()
      .Initialize(_csiSharedConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
    try
    {
      if (_store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      List<ICSiWrapper> rhinoObjects = modelCard
        .SendFilter.NotNull()
        .RefreshObjectIds()
        .Select(id => RhinoDoc.ActiveDoc.Objects.FindId(new Guid(id)))
        .Where(obj => obj != null)
        .ToList();

      if (rhinoObjects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<ICSiWrapper>>()
        .Execute(
          rhinoObjects,
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
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex).ConfigureAwait(false);
    }
  }

  public void CancelSend(string modelCardId)
  {
    _cancellationManager.CancelOperation(modelCardId);
  }
}
