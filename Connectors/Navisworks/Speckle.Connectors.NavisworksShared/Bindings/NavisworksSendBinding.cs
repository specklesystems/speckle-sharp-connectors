using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSendBinding : ISendBinding
{
  public string Name { get; }
  public IBrowserBridge Parent { get; }

  public List<ISendFilter> GetSendFilters() => throw new NotImplementedException();

  public List<ICardSetting> GetSendSettings() => throw new NotImplementedException();

  public Task Send(string modelCardId) => throw new NotImplementedException();

  public void CancelSend(string modelCardId) => throw new NotImplementedException();

  public SendBindingUICommands Commands { get; }
}
