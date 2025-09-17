using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.RevitShared.Operations;

/// <summary>
/// Constants for Revit connector settings IDs
/// </summary>
public static class RevitSettingsConstants
{
  // default values
  public const DetailLevelType DEFAULT_DETAIL_LEVEL = DetailLevelType.Medium;
  public const ReferencePointType DEFAULT_REFERENCE_POINT = ReferencePointType.InternalOrigin;

  // string ids
  public const string DETAIL_LEVEL = "detailLevel";
  public const string REFERENCE_POINT = "referencePoint";
  public const string SEND_NULL_EMPTY_PARAMS = "nullemptyparams";
  public const string INCLUDE_LINKED_MODELS = "includeLinkedModels";
  public const string SEND_REBARS_AS_VOLUMETRIC = "sendRebarsAsVolumetric";
}
