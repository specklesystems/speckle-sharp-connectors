using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class GeometryDetailLevelSetting(GeometryDetailLevel value) : ICardSetting
{
  public string? Id { get; set; } = "geometryDetailLevel";
  public string? Title { get; set; } = "Geometry Detail";
  public string? Description { get; set; } = "Controls geometry fidelity and conversion cost.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(GeometryDetailLevel)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, GeometryDetailLevel> GeometryDetailLevelMap = System
    .Enum.GetValues(typeof(GeometryDetailLevel))
    .Cast<GeometryDetailLevel>()
    .ToDictionary(v => v.ToString(), v => v);
}
