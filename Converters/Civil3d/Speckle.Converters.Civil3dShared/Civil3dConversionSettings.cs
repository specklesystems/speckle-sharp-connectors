namespace Speckle.Converters.Civil3d;

public record Civil3dConversionSettings
{
  public Document Document { get; init; }
  public string SpeckleUnits { get; init; }
}
