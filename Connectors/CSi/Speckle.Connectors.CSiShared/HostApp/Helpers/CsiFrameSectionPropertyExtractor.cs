using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Base frame section property extractor for CSi products.
/// </summary>
/// <remarks>
/// Handles common frame section properties using CSi API.
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
    dataExtractionResult.MaterialName = GetMaterialName(sectionName);
  }

  public string GetMaterialName(string sectionName)
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
    mechanicalProperties["crossSectionalArea"] = crossSectionalArea;
    mechanicalProperties["shearAreaInMajorAxisDirection"] = shearAreaInMajorAxisDirection;
    mechanicalProperties["shearAreaInMinorAxisDirection"] = shearAreaInMinorAxisDirection;
    mechanicalProperties["torsionalConstant"] = torsionalConstant;
    mechanicalProperties["momentOfInertiaAboutMajorAxis"] = momentOfInertiaAboutMajorAxis;
    mechanicalProperties["momentOfInertiaAboutMinorAxis"] = momentOfInertiaAboutMinorAxis;
    mechanicalProperties["sectionModulusAboutMajorAxis"] = sectionModulusAboutMajorAxis;
    mechanicalProperties["sectionModulusAboutMinorAxis"] = sectionModulusAboutMinorAxis;
    mechanicalProperties["plasticModulusAboutMajorAxis"] = plasticModulusAboutMajorAxis;
    mechanicalProperties["plasticModulusAboutMinorAxis"] = plasticModulusAboutMinorAxis;
    mechanicalProperties["radiusOfGyrationAboutMajorAxis"] = radiusOfGyrationAboutMajorAxis;
    mechanicalProperties["radiusOfGyrationAboutMinorAxis"] = radiusOfGyrationAboutMinorAxis;
  }
}
