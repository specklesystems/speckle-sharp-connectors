using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.Utils.Operations;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Models.Card;

public class SenderModelCard : ModelCard
{
  public ISendFilter? SendFilter { get; set; }

  // [JsonIgnore]
  // public HashSet<string> ChangedObjectIds { get; set; } = new();

  public SendInfo GetSendInfo(string hostApplication) =>
    new(AccountId.NotNull(), new Uri(ServerUrl.NotNull()), ProjectId.NotNull(), ModelId.NotNull(), hostApplication);
}
