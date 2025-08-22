using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class AddVisualizationProperties(bool value) : ICardSetting
{
  public string? Id { get; set; } = "addVisualizationProperties";
  public string? Title { get; set; } = "Add visualization properties for meshes (will increase model size)";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
