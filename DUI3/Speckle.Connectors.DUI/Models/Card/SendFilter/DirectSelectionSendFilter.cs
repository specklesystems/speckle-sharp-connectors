using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.DUI.Models.Card.SendFilter;

public abstract class DirectSelectionSendFilter : DiscriminatedObject, ISendFilter
{
  public string Id { get; set; } = "selection";
  public string Name { get; set; } = "Selection";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> ObjectIds { get; set; } = new();
  public Dictionary<string, string>? IdMap { get; set; } = new();

  public List<string> SelectedObjectIds { get; set; } = new();
  public abstract List<string> SetObjectIds();
}
