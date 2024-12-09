using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to joint elements across CSi products (e.g., ETABS, SAP2000)
/// using the PointObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Provides a focused interface for extracting properties specific to joint elements.
/// - Ensures consistency in property extraction logic across supported CSi products.
/// Integration:
/// - Part of the property extraction hierarchy.
/// - Used by <see cref="CsiGeneralPropertiesExtractor"/> for delegating joint property extraction.
/// </remarks>
public sealed class JointPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public JointPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Dictionary<string, object?>? ExtractProperties(CsiJointWrapper joint)
  {
    var properties = new Dictionary<string, object?>();
    properties["applicationId"] = GetApplicationId(joint);
    return properties;
  }

  private string GetApplicationId(CsiJointWrapper joint)
  {
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.PointObj.GetGUID(joint.Name, ref applicationId);
    return applicationId;
  }
}
