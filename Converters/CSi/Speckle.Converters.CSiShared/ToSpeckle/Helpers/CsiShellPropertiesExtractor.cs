using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to shell elements across CSi products (e.g., ETABS, SAP2000)
/// using the AreaObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Provides a focused interface for extracting properties specific to shell elements.
/// - Ensures consistency in property extraction logic across supported CSi products.
/// Integration:
/// - Part of the property extraction hierarchy.
/// - Used by <see cref="CsiGeneralPropertiesExtractor"/> for delegating shell property extraction.
/// </remarks>
public sealed class CsiShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiShellPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiShellWrapper shell, Dictionary<string, object?> properties)
  {
    properties["applicationId"] = GetApplicationId(shell);
  }

  private string GetApplicationId(CsiShellWrapper shell)
  {
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetGUID(shell.Name, ref applicationId);
    return applicationId;
  }
}
