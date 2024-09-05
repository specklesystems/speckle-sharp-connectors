using Rhino;

namespace Speckle.Converters.Rhino;

public record RhinoConversionSettings
{
  public RhinoDoc Document { get; init; }
  public string SpeckleUnits { get; init; }
}
