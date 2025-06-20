using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Models.Card;

public class SenderModelCard : ModelCard
{
  public ISendFilter? SendFilter { get; set; }

  // [JsonIgnore]
  // public HashSet<string> ChangedObjectIds { get; set; } = new();

  public SendInfo GetSendInfo(IAccountService accountService)
  {
    var account = accountService.GetAccountWithServerUrlFallback(AccountId.NotNull(), new Uri(ServerUrl.NotNull()));
    return new(account, ProjectId.NotNull(), ModelId.NotNull());
  }
}
