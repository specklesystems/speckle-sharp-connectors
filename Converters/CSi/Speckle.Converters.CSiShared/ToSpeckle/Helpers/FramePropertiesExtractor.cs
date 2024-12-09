using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to frame elements across CSi products (e.g., ETABS, SAP2000)
/// using the FrameObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Provides a focused interface for extracting properties specific to frame elements.
/// - Ensures consistency in property extraction logic across supported CSi products.
/// Integration:
/// - Part of the property extraction hierarchy.
/// - Used by <see cref="CsiGeneralPropertiesExtractor"/> for delegating frame property extraction.
/// </remarks>
public sealed class FramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public FramePropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Dictionary<string, object?>? ExtractProperties(CsiFrameWrapper frame)
  {
    var properties = new Dictionary<string, object?>();
    properties["applicationId"] = GetApplicationId(frame);
    return properties;
  }

  private string GetApplicationId(CsiFrameWrapper frame)
  {
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetGUID(frame.Name, ref applicationId);
    return applicationId;
  }
}
