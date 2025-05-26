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

public sealed class CsiSharedSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly IServiceProvider _serviceProvider;
  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<CsiSharedSendBinding> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ICsiConversionSettingsFactory _csiConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ISdkActivityFactory _activityFactory;

  public CsiSharedSendBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    ICancellationManager cancellationManager,
    IOperationProgressManager operationProgressManager,
    ILogger<CsiSharedSendBinding> logger,
    ICsiConversionSettingsFactory csiConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ISdkActivityFactory activityFactory,
    ICsiApplicationService csiApplicationService
  )
  {
    _store = store;
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
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<CsiConversionSettings>>()
        .Initialize(_csiConversionSettingsFactory.Create(_csiApplicationService.SapModel));

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

      List<ICsiWrapper> wrappers = modelCard
        .SendFilter.NotNull()
        .RefreshObjectIds()
        .Select(DecodeObjectIdentifier)
        .ToList();

      if (wrappers.Count == 0)
      {
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendResult = await scope
        .ServiceProvider.GetRequiredService<SendOperation<ICsiWrapper>>()
        .Execute(
          wrappers,
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

  private ICsiWrapper DecodeObjectIdentifier(string encodedId)
  {
    var (type, name) = ObjectIdentifier.Decode(encodedId);
    return CsiWrapperFactory.Create(type, name);
  }

  public void CancelSend(string modelCardId)
  {
    _cancellationManager.CancelOperation(modelCardId);
  }
}
