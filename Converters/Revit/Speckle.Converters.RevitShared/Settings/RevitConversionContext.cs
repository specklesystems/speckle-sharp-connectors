namespace Speckle.Converters.RevitShared.Settings;

public sealed record RevitConversionSettings(
  DB.Document Document,
  string SpeckleUnits,
  DetailLevelType DetailLevel,
  DB.Transform? ReferencePointTransform,
  double Tolerance = 0.01
)
{
  public const double DEFAULT_TOLERANCE = 0.0164042; // 5mm in ft
}
