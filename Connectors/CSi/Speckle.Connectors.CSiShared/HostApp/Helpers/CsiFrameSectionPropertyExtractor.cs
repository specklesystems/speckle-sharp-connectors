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
    Dictionary<string, object?> generalData = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
    generalData["Material"] = materialName;
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

    string distanceUnit = _settingsStore.Current.SpeckleUnits;
    string areaUnit = $"{distanceUnit}²"; // // TODO: Formalize this better
    string modulusUnit = $"{distanceUnit}³"; // // TODO: Formalize this better
    string inertiaUnit = $"{distanceUnit}\u2074"; // TODO: Formalize this better

    Dictionary<string, object?> mechanicalProperties = properties.EnsureNested(
      SectionPropertyCategory.SECTION_PROPERTIES
    );
    mechanicalProperties.AddWithUnits("Area", crossSectionalArea, areaUnit);
    mechanicalProperties.AddWithUnits("As2", shearAreaInMajorAxisDirection, areaUnit);
    mechanicalProperties.AddWithUnits("As3", shearAreaInMinorAxisDirection, areaUnit);
    mechanicalProperties.AddWithUnits("J", torsionalConstant, inertiaUnit);
    mechanicalProperties.AddWithUnits("I22", momentOfInertiaAboutMajorAxis, inertiaUnit);
    mechanicalProperties.AddWithUnits("I33", momentOfInertiaAboutMinorAxis, inertiaUnit);
    mechanicalProperties.AddWithUnits("S22", sectionModulusAboutMajorAxis, modulusUnit);
    mechanicalProperties.AddWithUnits("S33", sectionModulusAboutMinorAxis, modulusUnit);
    mechanicalProperties.AddWithUnits("Z22", plasticModulusAboutMajorAxis, modulusUnit);
    mechanicalProperties.AddWithUnits("Z33", plasticModulusAboutMinorAxis, modulusUnit);
    mechanicalProperties.AddWithUnits("R22", radiusOfGyrationAboutMajorAxis, distanceUnit);
    mechanicalProperties.AddWithUnits("R33", radiusOfGyrationAboutMinorAxis, distanceUnit);
  }

  private void GetPropertyModifiers(string sectionName, Dictionary<string, object?> properties)
  {
    double[] stiffnessModifiersArray = [];
    _settingsStore.Current.SapModel.PropFrame.GetModifiers(sectionName, ref stiffnessModifiersArray);

    Dictionary<string, object?> modifiers =
      new()
      {
        ["Cross-section (Axial) Area"] = stiffnessModifiersArray[0],
        ["Shear Area in 2 Direction"] = stiffnessModifiersArray[1],
        ["Shear Area in 3 Direction"] = stiffnessModifiersArray[2],
        ["Torsional Constant"] = stiffnessModifiersArray[3],
        ["Moment of Inertia about 2 Axis"] = stiffnessModifiersArray[4],
        ["Moment of Inertia about 3 Axis"] = stiffnessModifiersArray[5],
        ["Mass"] = stiffnessModifiersArray[6],
        ["Weight"] = stiffnessModifiersArray[7],
      };

    Dictionary<string, object?> generalData = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
    generalData["Modifiers"] = modifiers;
  }
}
