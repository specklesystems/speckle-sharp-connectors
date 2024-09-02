namespace Speckle.Converters.RevitShared.Settings;

public sealed record RevitConversionSettings(
  DB.Document Document,
  DetailLevelType DetailLevel,
  DB.Transform? ReferencePointTransform,
  double Tolerance = 0.01
);
