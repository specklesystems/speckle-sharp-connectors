using Speckle.Connectors.Utils;
using Speckle.Connectors.Utils.Operations;

namespace Speckle.Connectors.DUI.Models.Card;

public class ReceiverModelCard : ModelCard
{
  public string? ProjectName { get; set; }
  public string? ModelName { get; set; }
  public string? SelectedVersionId { get; set; }
  public string? LatestVersionId { get; set; }
  public bool HasDismissedUpdateWarning { get; set; }
  public List<string>? BakedObjectIds { get; set; }

  public ReceiveInfo GetReceiveInfo() =>
    new(
      AccountId.NotNull(),
      new Uri(ServerUrl.NotNull()),
      ProjectId.NotNull(),
      ProjectName.NotNull(),
      ModelName.NotNull(),
      SelectedVersionId.NotNull()
    );
}
