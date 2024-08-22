using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class GeometryFidelitySetting(object value) : ICardSetting
{
  public string? Id { get; set; } = "geometryFidelity";
  public string? Title { get; set; } = "Geometry Fidelity";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(GeometryFidelityType)).ToList();
  public object? Value { get; set; } = value;

  public static readonly Dictionary<string, GeometryFidelityType> GeometryFidelityMap = System
    .Enum.GetValues(typeof(GeometryFidelityType))
    .Cast<GeometryFidelityType>()
    .ToDictionary(v => v.ToString(), v => v);
}
