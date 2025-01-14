using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Base implementation for unpacking material properties common to all CSi products.
/// </summary>
/// <remarks>
/// Uses bulk extraction for efficiency, retrieving all materials in single API call.
/// Handles various material types (Isotropic, Orthotropic, etc.) through type-specific extraction.
/// Organizes properties in nested dictionaries matching CSi API structure.
/// </remarks>
public class SharedMaterialUnpacker : IMaterialUnpacker
{
  private readonly ILogger<SharedMaterialUnpacker> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ISdkActivityFactory _activityFactory;

  public SharedMaterialUnpacker(
    ILogger<SharedMaterialUnpacker> logger,
    ICsiApplicationService csiApplicationService,
    ISdkActivityFactory activityFactory
  )
  {
    _logger = logger;
    _csiApplicationService = csiApplicationService;
    _activityFactory = activityFactory;
  }

  public virtual List<IProxyCollection> UnpackMaterials(Collection rootObjectCollection)
  {
    try
    {
      using var activity = _activityFactory.Start("Unpack Materials");

      // Step 1: Get all defined materials
      int numberOfMaterials = 0;
      string[] materialNames = [];
      _csiApplicationService.SapModel.PropMaterial.GetNameList(ref numberOfMaterials, ref materialNames);

      Dictionary<string, IProxyCollection> materials = [];

      foreach (string materialName in materialNames)
      {
        try
        {
          var properties = ExtractCommonProperties(materialName);

          // 🫷 TODO: Scope a MaterialProxy class? Below is a temp solution. GroupProxy in this context not quite right.
          GroupProxy materialProxy =
            new()
            {
              id = materialName,
              name = materialName,
              applicationId = materialName,
              objects = [],
              ["Properties"] = properties
            };

          materials[materialName] = materialProxy;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogError(ex, "Failed to create material proxy for {MaterialName}", materialName);
        }
      }

      var materialProxies = materials.Values.ToList();
      if (materialProxies.Count > 0)
      {
        rootObjectCollection[ProxyKeys.MATERIAL] = materialProxies;
      }

      return materialProxies;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to unpack materials");
      return [];
    }
  }

  protected virtual Dictionary<string, object?> ExtractCommonProperties(string materialName)
  {
    var properties = new Dictionary<string, object?>();

    ExtractGeneralProperties(materialName, properties);
    ExtractWeightAndMassProperties(materialName, properties);
    ExtractMechanicalProperties(materialName, properties);

    return properties;
  }

  private void ExtractGeneralProperties(string materialName, Dictionary<string, object?> properties)
  {
    eMatType materialType = eMatType.Steel;
    int materialColor = 0;
    string materialNotes = string.Empty;
    string materialGuid = string.Empty;

    _csiApplicationService.SapModel.PropMaterial.GetMaterial(
      materialName,
      ref materialType,
      ref materialColor,
      ref materialNotes,
      ref materialGuid
    );

    var generalData = DictionaryUtils.EnsureNestedDictionary(properties, "General Data");
    generalData["name"] = materialName;
    generalData["type"] = materialType.ToString();
    generalData["notes"] = materialNotes;
  }

  private void ExtractWeightAndMassProperties(string materialName, Dictionary<string, object?> properties)
  {
    double weightPerUnitVolume = double.NaN;
    double massPerUnitVolume = double.NaN;

    _csiApplicationService.SapModel.PropMaterial.GetWeightAndMass(
      materialName,
      ref weightPerUnitVolume,
      ref massPerUnitVolume
    );

    var weightAndMass = DictionaryUtils.EnsureNestedDictionary(properties, "Weight and Mass");
    weightAndMass["weightPerUnitVolume"] = weightPerUnitVolume;
    weightAndMass["massPerUnitVolume"] = massPerUnitVolume;
  }

  private void ExtractMechanicalProperties(string materialName, Dictionary<string, object?> properties)
  {
    int materialDirectionalSymmetryKey = 0;
    eMatType materialType = eMatType.Steel;

    _csiApplicationService.SapModel.PropMaterial.GetTypeOAPI(
      materialName,
      ref materialType,
      ref materialDirectionalSymmetryKey
    );

    var mechanicalProperties = DictionaryUtils.EnsureNestedDictionary(properties, "Mechanical Properties");
    mechanicalProperties["directionalSymmetryType"] = materialDirectionalSymmetryKey switch
    {
      1 => "Isotropic",
      2 => "Orthotropic",
      3 => "Anisotropic",
      4 => "Uniaxial",
      _ => $"Unknown ({materialDirectionalSymmetryKey})"
    };

    ExtractMechanicalPropertiesByType(materialName, materialDirectionalSymmetryKey, mechanicalProperties);
  }

  private void ExtractMechanicalPropertiesByType(
    string materialName,
    int symmetryType,
    Dictionary<string, object?> mechanicalProperties
  )
  {
    switch (symmetryType)
    {
      case 1:
        ExtractIsotropicProperties(materialName, mechanicalProperties);
        break;
      case 2:
        ExtractOrthotropicProperties(materialName, mechanicalProperties);
        break;
      case 3:
        ExtractAnisotropicProperties(materialName, mechanicalProperties);
        break;
      case 4:
        ExtractUniaxialProperties(materialName, mechanicalProperties);
        break;
    }
  }

  private void ExtractIsotropicProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double modulusOfElasticity = double.NaN;
    double poissonRatio = double.NaN;
    double thermalCoefficient = double.NaN;
    double shearModulus = double.NaN;

    _csiApplicationService.SapModel.PropMaterial.GetMPIsotropic(
      materialName,
      ref modulusOfElasticity,
      ref poissonRatio,
      ref thermalCoefficient,
      ref shearModulus
    );

    mechanicalProperties["modulusOfElasticity"] = modulusOfElasticity;
    mechanicalProperties["poissonRatio"] = poissonRatio;
    mechanicalProperties["thermalCoefficient"] = thermalCoefficient;
    mechanicalProperties["shearModulus"] = shearModulus;
  }

  private void ExtractOrthotropicProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double[] modulusOfElasticityArray = Array.Empty<double>();
    double[] poissonRatioArray = Array.Empty<double>();
    double[] thermalCoefficientArray = Array.Empty<double>();
    double[] shearModulusArray = Array.Empty<double>();

    _csiApplicationService.SapModel.PropMaterial.GetMPOrthotropic(
      materialName,
      ref modulusOfElasticityArray,
      ref poissonRatioArray,
      ref thermalCoefficientArray,
      ref shearModulusArray
    );

    mechanicalProperties["modulusOfElasticityArray"] = modulusOfElasticityArray;
    mechanicalProperties["poissonRatioArray"] = poissonRatioArray;
    mechanicalProperties["thermalCoefficientArray"] = thermalCoefficientArray;
    mechanicalProperties["shearModulusArray"] = shearModulusArray;
  }

  private void ExtractAnisotropicProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double[] modulusOfElasticityArray = Array.Empty<double>();
    double[] poissonRatioArray = Array.Empty<double>();
    double[] thermalCoefficientArray = Array.Empty<double>();
    double[] shearModulusArray = Array.Empty<double>();

    _csiApplicationService.SapModel.PropMaterial.GetMPAnisotropic(
      materialName,
      ref modulusOfElasticityArray,
      ref poissonRatioArray,
      ref thermalCoefficientArray,
      ref shearModulusArray
    );

    mechanicalProperties["modulusOfElasticityArray"] = modulusOfElasticityArray;
    mechanicalProperties["poissonRatioArray"] = poissonRatioArray;
    mechanicalProperties["thermalCoefficientArray"] = thermalCoefficientArray;
    mechanicalProperties["shearModulusArray"] = shearModulusArray;
  }

  private void ExtractUniaxialProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double modulusOfElasticity = double.NaN;
    double thermalCoefficient = double.NaN;

    _csiApplicationService.SapModel.PropMaterial.GetMPUniaxial(
      materialName,
      ref modulusOfElasticity,
      ref thermalCoefficient
    );

    mechanicalProperties["modulusOfElasticity"] = modulusOfElasticity;
    mechanicalProperties["thermalCoefficient"] = thermalCoefficient;
  }
}
