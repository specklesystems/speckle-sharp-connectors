using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.TSDShared.Bindings;

internal sealed class TSDSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }
  public SendBindingUICommands Commands { get; }

  private readonly List<ISendFilter> _sendFilters;

  public TSDSendBinding(IBrowserBridge parent, IEnumerable<ISendFilter> sendFilters)
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _sendFilters = sendFilters.ToList();
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public Task Send(string modelCardId) =>
    throw new NotImplementedException("Sending is not implemented yet for the TSD connector.");

  public void CancelSend(string modelCardId) { }
}
