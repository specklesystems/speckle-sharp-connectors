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

    var generalData = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
    generalData["Property Type"] = propertyTypeValue.ToString();
  }

  private void GetPropertyModifiers(string sectionName, Dictionary<string, object?> properties)
  {
    double[] stiffnessModifiersArray = [];
    _settingsStore.Current.SapModel.PropArea.GetModifiers(sectionName, ref stiffnessModifiersArray);

    Dictionary<string, object?> modifiers =
      new()
      {
        ["Membrane f11 Direction"] = stiffnessModifiersArray[0],
        ["Membrane f22 Direction"] = stiffnessModifiersArray[1],
        ["Membrane f12 Direction"] = stiffnessModifiersArray[2],
        ["Bending m11 Direction"] = stiffnessModifiersArray[3],
        ["Bending m22 Direction"] = stiffnessModifiersArray[3],
        ["Bending m12 Direction"] = stiffnessModifiersArray[4],
        ["Shear v13 Direction"] = stiffnessModifiersArray[5],
        ["Shear v23 Direction"] = stiffnessModifiersArray[6],
        ["Mass"] = stiffnessModifiersArray[7],
        ["Weight"] = stiffnessModifiersArray[8],
      };

    var generalData = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
    generalData["Modifiers"] = modifiers;
  }
}
