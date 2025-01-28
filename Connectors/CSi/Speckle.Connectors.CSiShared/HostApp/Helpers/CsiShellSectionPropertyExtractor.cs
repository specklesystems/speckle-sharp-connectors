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

  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties)
  {
    GetPropertyType(sectionName, properties);
    GetPropertyModifiers(sectionName, properties);
  }

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

    var generalData = DictionaryUtils.EnsureNestedDictionary(properties, SectionPropertyCategory.GENERAL_DATA);
    generalData["propertyType"] = propertyTypeValue;
  }

  private void GetPropertyModifiers(string sectionName, Dictionary<string, object?> properties)
  {
    double[] stiffnessModifiersArray = [];
    _settingsStore.Current.SapModel.PropArea.GetModifiers(sectionName, ref stiffnessModifiersArray);

    Dictionary<string, object?> modifiers =
      new()
      {
        ["f11"] = stiffnessModifiersArray[0],
        ["f22"] = stiffnessModifiersArray[1],
        ["f12"] = stiffnessModifiersArray[2],
        ["m11"] = stiffnessModifiersArray[3],
        ["m22"] = stiffnessModifiersArray[3],
        ["m12"] = stiffnessModifiersArray[4],
        ["v13"] = stiffnessModifiersArray[5],
        ["v23"] = stiffnessModifiersArray[6],
        ["mass"] = stiffnessModifiersArray[7],
        ["weight"] = stiffnessModifiersArray[8]
      };

    var generalData = DictionaryUtils.EnsureNestedDictionary(properties, SectionPropertyCategory.GENERAL_DATA);
    generalData["modifiers"] = modifiers;
  }
}
