namespace Speckle.Converters.RevitShared.Settings;

public enum GeometryFidelityType
{
  Coarse,
  Medium,
  Fine
}

public record ToSpeckleSettings(GeometryFidelityType GeometryFidelity);
