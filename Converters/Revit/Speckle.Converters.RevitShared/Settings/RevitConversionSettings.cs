﻿namespace Speckle.Converters.RevitShared.Settings;

public record RevitConversionSettings(
  DB.Document Document,
  DetailLevelType DetailLevel,
  DB.Transform? ReferencePointTransform,
  string SpeckleUnits,
  bool SendParameterNullOrEmptyStrings,
  double Tolerance = 0.0164042 // 5mm in ft
);
