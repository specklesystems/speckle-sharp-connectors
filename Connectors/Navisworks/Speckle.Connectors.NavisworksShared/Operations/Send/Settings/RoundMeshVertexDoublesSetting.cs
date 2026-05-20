using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class RoundMeshVertexDoublesSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "roundMeshVertexDoubles";
  public string? Title { get; set; } = "Round Mesh Vertex Doubles";
  public string? Description { get; set; } = "QA/QC toggle for unit-aware vertex rounding before serialization.";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
