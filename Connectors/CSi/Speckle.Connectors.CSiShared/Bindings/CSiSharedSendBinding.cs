using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
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
  private readonly ICSiApplicationService _csiApplicationService; // Update selection binding to centralized CSiSharedApplicationService instead of trying to maintain a reference to "sapModel"
  private readonly ICSiConversionSettingsFactory _csiConversionSettingsFactory;
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
    ICSiConversionSettingsFactory csiConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory,
    ICSiApplicationService csiApplicationService
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
    _csiConversionSettingsFactory = csiConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    _activityFactory = activityFactory;
    _csiApplicationService = csiApplicationService;
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

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
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<CSiConversionSettings>>()
        .Initialize(_csiConversionSettingsFactory.Create(_csiApplicationService.SapModel));

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      List<ICSiWrapper> wrappers = modelCard
        .SendFilter.NotNull()
        .RefreshObjectIds()
        .Select(DecodeObjectIdentifier)
        .ToList();

      if (wrappers.Count == 0)
      {
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<ICSiWrapper>>()
        .Execute(
          wrappers,
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

  private ICSiWrapper DecodeObjectIdentifier(string encodedId)
  {
    var (type, name) = ObjectIdentifier.Decode(encodedId);
    return CSiWrapperFactory.Create(type, name);
  }

  public void CancelSend(string modelCardId)
  {
    _cancellationManager.CancelOperation(modelCardId);
  }
}
