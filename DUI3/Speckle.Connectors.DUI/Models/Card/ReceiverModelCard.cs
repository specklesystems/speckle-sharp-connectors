namespace Speckle.Connectors.DUI.Models.Card;

public class ReceiverModelCard : ModelCard
{
  public string? ProjectName { get; set; }
  public string? ModelName { get; set; }
  public string? SelectedVersionId { get; set; }
  public string? SelectedVersionSourceApp { get; set; }
  public string? SelectedVersionUserId { get; set; }
  public string? LatestVersionId { get; set; }
  public string? LatestVersionSourceApp { get; set; }
  public string? LatestVersionUserId { get; set; }
  public bool HasDismissedUpdateWarning { get; set; }
  public List<string>? BakedObjectIds { get; set; }
}
