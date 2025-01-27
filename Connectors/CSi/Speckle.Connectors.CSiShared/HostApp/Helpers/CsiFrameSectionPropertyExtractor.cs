using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Base frame section property extractor for CSi products.
/// </summary>
/// <remarks>
/// Handles common Csi API calls for frame section properties
/// Provides foundation for application-specific extractors.
/// </remarks>
public class CsiFrameSectionPropertyExtractor : IFrameSectionPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiFrameSectionPropertyExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties)
  {
    GetMaterialName(sectionName, properties);
    GetSectionProperties(sectionName, properties);
    GetPropertyModifiers(sectionName, properties);
  }

  private void GetMaterialName(string sectionName, Dictionary<string, object?> properties)
  {
    // get material name
    string materialName = string.Empty;
    _settingsStore.Current.SapModel.PropFrame.GetMaterial(sectionName, ref materialName);

    // append to General Data of properties dictionary
    Dictionary<string, object?> generalData = DictionaryUtils.EnsureNestedDictionary(
      properties,
      SectionPropertyCategory.GENERAL_DATA
    );
    generalData["material"] = materialName;
  }

  private void GetSectionProperties(string sectionName, Dictionary<string, object?> properties)
  {
    double crossSectionalArea = 0,
      shearAreaInMajorAxisDirection = 0,
      shearAreaInMinorAxisDirection = 0,
      torsionalConstant = 0,
      momentOfInertiaAboutMajorAxis = 0,
      momentOfInertiaAboutMinorAxis = 0,
      sectionModulusAboutMajorAxis = 0,
      sectionModulusAboutMinorAxis = 0,
      plasticModulusAboutMajorAxis = 0,
      plasticModulusAboutMinorAxis = 0,
      radiusOfGyrationAboutMajorAxis = 0,
      radiusOfGyrationAboutMinorAxis = 0;

    _settingsStore.Current.SapModel.PropFrame.GetSectProps(
      sectionName,
      ref crossSectionalArea,
      ref shearAreaInMajorAxisDirection,
      ref shearAreaInMinorAxisDirection,
      ref torsionalConstant,
      ref momentOfInertiaAboutMajorAxis,
      ref momentOfInertiaAboutMinorAxis,
      ref sectionModulusAboutMajorAxis,
      ref sectionModulusAboutMinorAxis,
      ref plasticModulusAboutMajorAxis,
      ref plasticModulusAboutMinorAxis,
      ref radiusOfGyrationAboutMajorAxis,
      ref radiusOfGyrationAboutMinorAxis
    );

    Dictionary<string, object?> mechanicalProperties = DictionaryUtils.EnsureNestedDictionary(
      properties,
      SectionPropertyCategory.SECTION_PROPERTIES
    );
    mechanicalProperties["area"] = crossSectionalArea;
    mechanicalProperties["As2"] = shearAreaInMajorAxisDirection;
    mechanicalProperties["As3"] = shearAreaInMinorAxisDirection;
    mechanicalProperties["torsion"] = torsionalConstant;
    mechanicalProperties["I22"] = momentOfInertiaAboutMajorAxis;
    mechanicalProperties["I33"] = momentOfInertiaAboutMinorAxis;
    mechanicalProperties["S22"] = sectionModulusAboutMajorAxis;
    mechanicalProperties["S33"] = sectionModulusAboutMinorAxis;
    mechanicalProperties["Z22"] = plasticModulusAboutMajorAxis;
    mechanicalProperties["Z33"] = plasticModulusAboutMinorAxis;
    mechanicalProperties["R22"] = radiusOfGyrationAboutMajorAxis;
    mechanicalProperties["R33"] = radiusOfGyrationAboutMinorAxis;
  }

  private void GetPropertyModifiers(string sectionName, Dictionary<string, object?> properties)
  {
    double[] stiffnessModifiersArray = [];
    _settingsStore.Current.SapModel.PropFrame.GetModifiers(sectionName, ref stiffnessModifiersArray);

    Dictionary<string, object?> modifiers =
      new()
      {
        ["crossSectionalAreaModifier"] = stiffnessModifiersArray[0],
        ["shearAreaInLocal2DirectionModifier"] = stiffnessModifiersArray[1],
        ["shearAreaInLocal3DirectionModifier"] = stiffnessModifiersArray[2],
        ["torsionalConstantModifier"] = stiffnessModifiersArray[3],
        ["momentOfInertiaAboutLocal2AxisModifier"] = stiffnessModifiersArray[4],
        ["momentOfInertiaAboutLocal3AxisModifier"] = stiffnessModifiersArray[5],
        ["mass"] = stiffnessModifiersArray[6],
        ["weight"] = stiffnessModifiersArray[7],
      };

    Dictionary<string, object?> generalData = DictionaryUtils.EnsureNestedDictionary(
      properties,
      SectionPropertyCategory.GENERAL_DATA
    );
    generalData["modifiers"] = modifiers;
  }
}
