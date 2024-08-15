using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.DUI.Bindings;

public interface ISendBinding : IBinding
{
  public List<ISendFilter> GetSendFilters();

  public List<CardSetting> GetSendSettings();

  /// <summary>
  /// Instructs the host app to start sending this model.
  /// </summary>
  /// <param name="modelCardId"></param>
  public Task Send(string modelCardId);

  /// <summary>
  /// Instructs the host app to  cancel the sending for a given model.
  /// </summary>
  /// <param name="modelCardId"></param>
  public void CancelSend(string modelCardId);

  public SendBindingUICommands Commands { get; }
}
