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

  public void ExtractProperties(string sectionName, SectionPropertyExtractionResult dataExtractionResult)
  {
    GetSectionProperties(sectionName, dataExtractionResult.Properties);
    GetPropertyModifiers(sectionName, dataExtractionResult.Properties);
    dataExtractionResult.MaterialName = GetMaterialName(sectionName);
  }

  private string GetMaterialName(string sectionName)
  {
    string materialName = string.Empty;
    _settingsStore.Current.SapModel.PropFrame.GetMaterial(sectionName, ref materialName);
    return materialName;
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

    var mechanicalProperties = DictionaryUtils.EnsureNestedDictionary(properties, "Section Properties");
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

    var generalData = DictionaryUtils.EnsureNestedDictionary(properties, "General Data");
    generalData["modifiers"] = modifiers;
  }
}
