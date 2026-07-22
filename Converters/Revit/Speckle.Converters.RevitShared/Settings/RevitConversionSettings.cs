namespace Speckle.Converters.RevitShared.Settings;

public record RevitConversionSettings(
  DB.Document Document,
  DetailLevelType DetailLevel,
  DB.Transform? ReferencePointTransform,
  string SpeckleUnits,
  bool SendParameterNullOrEmptyStrings,
  bool SendLinkedModels,
  bool SendRebarsAsVolumetric,
  bool SendAreasAsMesh,
  bool ReceiveInstancesAsFamilies,
  double Tolerance = 0.0164042, // 5mm in ft
  // The requested reference-point kind alongside its resolved ReferencePointTransform. Carried so the 4.0 send
  // pipeline can record it in the bundle meta (ENG-8947); ReferencePointTransform alone can't distinguish
  // projectBasePoint from surveyPoint. Send-only; defaults to InternalOrigin (receive leaves it untouched).
  ReferencePointType ReferencePointKind = ReferencePointType.InternalOrigin
);
