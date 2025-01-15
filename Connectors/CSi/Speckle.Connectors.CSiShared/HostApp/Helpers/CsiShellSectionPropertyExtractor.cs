using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Base shell section property extractor for CSi products.
/// </summary>
/// <remarks>
/// Handles common Csi API calls for shell section properties.
/// Provides foundation for application-specific extractors.
/// </remarks>
public class CsiShellSectionPropertyExtractor : IShellSectionPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiShellSectionPropertyExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(string sectionName, SectionPropertyExtractionResult dataExtractionResult)
  {
    GetPropertyType(sectionName, dataExtractionResult.Properties);
    GetPropertyModifiers(sectionName, dataExtractionResult.Properties);
  }

  public string GetMaterialName(string sectionName) => throw new NotImplementedException();

  private void GetPropertyType(string sectionName, Dictionary<string, object?> properties)
  {
    int propertyTypeKey = 1;
    _settingsStore.Current.SapModel.PropArea.GetTypeOAPI(sectionName, ref propertyTypeKey);
    var propertyTypeValue = propertyTypeKey switch
    {
      1 => AreaPropertyType.SHELL,
      2 => AreaPropertyType.PLANE,
      3 => AreaPropertyType.ASOLID,
      _ => throw new ArgumentException($"Unknown property type: {propertyTypeKey}"),
    };

    var generalData = DictionaryUtils.EnsureNestedDictionary(properties, "General Data");
    generalData["propertyType"] = propertyTypeValue;
  }

  private void GetPropertyModifiers(string sectionName, Dictionary<string, object?> properties)
  {
    double[] stiffnessModifiersArray = [];
    _settingsStore.Current.SapModel.PropArea.GetModifiers(sectionName, ref stiffnessModifiersArray);

    var modifierKeys = new[] { "f11", "f22", "f12", "m11", "m22", "m12", "v13", "v23", "mass", "weight" };
    var modifiers = modifierKeys
      .Zip(stiffnessModifiersArray, (key, value) => (key, value))
      .ToDictionary(x => x.key, x => (object?)x.value);

    var generalData = DictionaryUtils.EnsureNestedDictionary(properties, "General Data");
    generalData["modifiers"] = modifiers;
  }
}
