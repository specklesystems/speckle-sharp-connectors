using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.ETABS22.Bindings;

public sealed class ETABSSendBinding : ISendBinding
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
  private readonly ILogger<ETABSSendBinding> _logger;

  public ETABSSendBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    IServiceProvider serviceProvider,
    CancellationManager cancellationManager,
    IOperationProgressManager operationProgressManager,
    ILogger<ETABSSendBinding> logger
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
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    // placeholder for actual send implementation
    await Task.CompletedTask.ConfigureAwait(false);
  }

  public void CancelSend(string modelCardId)
  {
    _cancellationManager.CancelOperation(modelCardId);
  }
}
