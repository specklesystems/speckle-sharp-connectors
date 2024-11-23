using Speckle.Connector.Navisworks.Filters;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly CancellationManager _cancellationManager;

  public NavisworksSendBinding(IBrowserBridge parent, DocumentModelStore store, CancellationManager cancellationManager)
  {
    Parent = parent;
    _store = store;
    _cancellationManager = cancellationManager;
  }

  public List<ISendFilter> GetSendFilters() => [new NavisworksSelectionFilter()];

  public List<ICardSetting> GetSendSettings() => [];

  public Task Send(string modelCardId) => throw new NotImplementedException();

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public SendBindingUICommands Commands { get; }

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
