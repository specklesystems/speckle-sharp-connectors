using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class AddVisualizationProperties(bool value) : ICardSetting
{
  public string? Id { get; set; } = "addVisualizationProperties";
  public string? Title { get; set; } = "Add Mesh Visualization Properties";
  public string? Description { get; set; } = "Embeds vertex colours and texture coordinates so meshes render as they do in Rhino. Will increase model size.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
