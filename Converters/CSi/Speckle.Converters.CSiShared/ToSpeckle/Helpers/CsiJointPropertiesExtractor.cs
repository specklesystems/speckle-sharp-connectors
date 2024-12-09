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
public sealed class CsiJointPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiJointPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiJointWrapper joint, Dictionary<string, object?> properties)
  {
    properties["applicationId"] = GetApplicationId(joint);
  }

  private string GetApplicationId(CsiJointWrapper joint)
  {
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.PointObj.GetGUID(joint.Name, ref applicationId);
    return applicationId;
  }
}
