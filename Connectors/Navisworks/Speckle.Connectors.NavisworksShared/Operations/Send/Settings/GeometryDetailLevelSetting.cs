using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class GeometryDetailLevelSetting(GeometryDetailLevel value) : ICardSetting
{
  public string? Id { get; set; } = "geometryDetailLevel";
  public string? Title { get; set; } = "Geometry Detail";
  public string? Description { get; set; } =
    "Full, Optimised (65 degree seam-aware + unit-aware rounding), or Lite.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } =
  [
    GeometryDetailLevel.Full.ToString(),
    GeometryDetailLevel.Optimised.ToString(),
    GeometryDetailLevel.Lite.ToString(),
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
    result["Optimized"] = GeometryDetailLevel.Optimised;
    result["Optimised"] = GeometryDetailLevel.Optimised;
    result["OptimizedModerate"] = GeometryDetailLevel.Optimised;
    result["OptimizedMajor"] = GeometryDetailLevel.Optimised;
    result["OptimizedSeams25"] = GeometryDetailLevel.Optimised;
    result["OptimizedSeams60"] = GeometryDetailLevel.Optimised;
    result["OptimizedAggressive"] = GeometryDetailLevel.Optimised;
    result["SpatialLite"] = GeometryDetailLevel.Lite;
    return result;
  }
}
