using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Tekla2024.Bindings;

public class TeklaSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }
  private readonly List<ISendFilter> _sendFilters;

  public TeklaSendBinding(IBrowserBridge parent, IEnumerable<ISendFilter> sendFilters)
  {
    Parent = parent;
    _sendFilters = sendFilters.ToList();
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public Task Send(string modelCardId) => Task.CompletedTask;

  public void CancelSend(string modelCardId) => throw new NotImplementedException();

  public SendBindingUICommands Commands { get; }
}
