using Autodesk.Revit.DB;

namespace Speckle.Converters.RevitShared.Settings;

public enum DetailLevelType
{
  Coarse,
  Medium,
  Fine
}

public enum ReferencePointType
{
  InternalOrigin,
  ProjectBase,
  Survey
}

public record ToSpeckleSettings(DetailLevelType DetailLevel, Transform? ReferencePointTransform);
