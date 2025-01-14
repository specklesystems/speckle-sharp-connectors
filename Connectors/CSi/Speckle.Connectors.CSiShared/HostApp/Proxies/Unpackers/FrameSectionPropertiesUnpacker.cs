using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Base implementation for extracting frame section properties from CSi products.
/// </summary>
/// <remarks>
/// Provides common CSi section properties with extension points for application-specific data.
/// Properties organized in nested dictionaries matching CSi API structure.
/// Design follows template method pattern for property extraction customization.
/// </remarks>
public class FrameSectionPropertiesUnpacker
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ILogger<FrameSectionPropertiesUnpacker> _logger;

  protected FrameSectionPropertiesUnpacker(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ILogger<FrameSectionPropertiesUnpacker> logger
  )
  {
    _settingsStore = settingsStore;
    _logger = logger;
  }

  public Dictionary<string, object?> GetProperties(string sectionName)
  {
    var properties = new Dictionary<string, object?>();
    ExtractCommonProperties(sectionName, properties);
    ExtractTypeSpecificProperties(sectionName, properties);
    return properties;
  }

  protected void ExtractCommonProperties(string sectionName, Dictionary<string, object?> properties)
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

  // Virtual instead of abstract, with empty default implementation
  protected virtual void ExtractTypeSpecificProperties(string sectionName, Dictionary<string, object?> properties)
  {
    // Base implementation does nothing
  }

  public string GetMaterialName(string sectionName)
  {
    string materialName = string.Empty;
    _settingsStore.Current.SapModel.PropFrame.GetMaterial(sectionName, ref materialName);
    return materialName;
  }
}
