using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class GeometryDetailLevelSetting(GeometryDetailLevel value) : ICardSetting
{
  public string? Id { get; set; } = "geometryDetailLevel";
  public string? Title { get; set; } = "Geometry Detail";
  public string? Description { get; set; } =
    "Geometry fidelity presets. OptimizedSeams25/60 bundle seam-aware welding with crease angle. OptimizedAggressive uses maximum weld (smallest payload).";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } =
  [
    GeometryDetailLevel.Full.ToString(),
    GeometryDetailLevel.OptimizedSeams25.ToString(),
    GeometryDetailLevel.OptimizedSeams60.ToString(),
    GeometryDetailLevel.OptimizedAggressive.ToString(),
    GeometryDetailLevel.SpatialLite.ToString(),
  ];
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, GeometryDetailLevel> GeometryDetailLevelMap = BuildGeometryDetailLevelMap();

  private static Dictionary<string, GeometryDetailLevel> BuildGeometryDetailLevelMap()
  {
    var result = System
      .Enum.GetValues(typeof(GeometryDetailLevel))
      .Cast<GeometryDetailLevel>()
      .ToDictionary(v => v.ToString(), v => v);
    // Backward compatibility for saved send cards
    result["Optimized"] = GeometryDetailLevel.OptimizedSeams25;
    result["OptimizedModerate"] = GeometryDetailLevel.OptimizedSeams25;
    result["OptimizedMajor"] = GeometryDetailLevel.OptimizedAggressive;
    return result;
  }
}
