using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.TSDShared.Bindings;

internal sealed class TSDSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }
  public SendBindingUICommands Commands { get; }

  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;
  private readonly ITSDApplicationService _applicationService;

  public TSDSendBinding(
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ISendOperationManagerFactory sendOperationManagerFactory,
    ITSDApplicationService applicationService
  )
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    _applicationService = applicationService;
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (_, _) => { },
      async card =>
        await _applicationService
          .GetMembersForSendAsync(card.SendFilter.NotNull().RefreshObjectIds())
          .ConfigureAwait(false),
      null,
      null
    );
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
