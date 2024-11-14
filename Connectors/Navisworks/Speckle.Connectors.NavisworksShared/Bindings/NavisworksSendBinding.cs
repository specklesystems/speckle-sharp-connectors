using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }

  public List<ISendFilter> GetSendFilters() => [];

  public List<ICardSetting> GetSendSettings() => [];

  public Task Send(string modelCardId) => throw new NotImplementedException();

  public void CancelSend(string modelCardId) => throw new NotImplementedException();

  public SendBindingUICommands Commands { get; }
}
