using Speckle.Connectors.DUI.Models.Card;

namespace Speckle.Connectors.RevitShared.DUI;

public class RevitSenderModelCard : SenderModelCard
{
  public Dictionary<string, SendFilterObjectIdentifier> SendFilterObjectIdentifiers { get; set; }
}

public class SendFilterObjectIdentifier
{
  public string UniqueId { get; set; }
  public string? CategoryId { get; set; }
}
