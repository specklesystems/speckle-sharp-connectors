namespace Speckle.Connectors.DUI.Models.Card.SendFilter;

public interface ISendFilter
{
  public string Id { get; set; }
  public string Name { get; set; }
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> SelectedObjectIds { get; set; }
  public Dictionary<string, string>? IdMap { get; set; }

  /// <summary>
  /// Refreshes the ids of the objects from the filter.
  /// In Revit we re-fetch the new objects before send or whenever new element added into specific type of filter.
  /// i.e. we have category filter with "Walls" selected, whenever user added new wall we need to update the ObjectIds before
  /// running expiration checks to be able to catch the model card is not up-to-date anymore
  /// </summary>
  /// <returns></returns>
  public List<string> RefreshObjectIds();
}
