namespace Speckle.Connectors.DUI.Models;

public sealed class ReceiverSettingsChangedEventArgs(string modelCardId) : EventArgs
{
  public string ModelCardId { get; } = modelCardId;
}
