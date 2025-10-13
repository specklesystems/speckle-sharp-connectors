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

  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties) =>
    GetSectionProperties(sectionName, properties);

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

    Dictionary<string, object?> mechanicalProperties = properties.EnsureNested(
      SectionPropertyCategory.SECTION_PROPERTIES
    );
    mechanicalProperties.Add("Area", crossSectionalArea);
    mechanicalProperties.Add("As2", shearAreaInMajorAxisDirection);
    mechanicalProperties.Add("As3", shearAreaInMinorAxisDirection);
    mechanicalProperties.Add("J", torsionalConstant);
    mechanicalProperties.Add("I22", momentOfInertiaAboutMajorAxis);
    mechanicalProperties.Add("I33", momentOfInertiaAboutMinorAxis);
    mechanicalProperties.Add("S22", sectionModulusAboutMajorAxis);
    mechanicalProperties.Add("S33", sectionModulusAboutMinorAxis);
    mechanicalProperties.Add("Z22", plasticModulusAboutMajorAxis);
    mechanicalProperties.Add("Z33", plasticModulusAboutMinorAxis);
    mechanicalProperties.Add("R22", radiusOfGyrationAboutMajorAxis);
    mechanicalProperties.Add("R33", radiusOfGyrationAboutMinorAxis);
  }
}
