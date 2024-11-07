namespace Speckle.Connectors.DUI.Models.Card.SendFilter;

public interface ISendFilter
{
  public string Id { get; set; }
  public string Name { get; set; }
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> ObjectIds { get; set; }
  public Dictionary<string, string>? IdMap { get; set; }

  /// <summary>
  /// Gets the ids of the objects targeted by the filter from the host application.
  /// </summary>
  /// <returns></returns>
  public List<string> SetObjectIds();
}
