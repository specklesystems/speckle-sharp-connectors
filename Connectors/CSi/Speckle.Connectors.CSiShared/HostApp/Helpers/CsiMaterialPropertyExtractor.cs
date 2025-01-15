using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Base material property extractor for CSi products.
/// </summary>
/// <remarks>
/// Currently, all material property extraction can happen on a CsiShared level which simplifies things a lot.
/// Properties depend on the directional symmetry of the material, hence the switch statements.
/// </remarks>
public class CsiMaterialPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiMaterialPropertyExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(string materialName, Dictionary<string, object?> properties)
  {
    GetGeneralProperties(materialName, properties);
    GetWeightAndMassProperties(materialName, properties);
    GetMechanicalProperties(materialName, properties);
  }

  private void GetGeneralProperties(string materialName, Dictionary<string, object?> properties)
  {
    {
      eMatType materialType = default;
      int materialColor = 0;
      string materialNotes = string.Empty;
      string materialGuid = string.Empty;

      _settingsStore.Current.SapModel.PropMaterial.GetMaterial(
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
  }

  private void GetWeightAndMassProperties(string materialName, Dictionary<string, object?> properties)
  {
    double weightPerUnitVolume = double.NaN;
    double massPerUnitVolume = double.NaN;

    _settingsStore.Current.SapModel.PropMaterial.GetWeightAndMass(
      materialName,
      ref weightPerUnitVolume,
      ref massPerUnitVolume
    );

    var weightAndMass = DictionaryUtils.EnsureNestedDictionary(properties, "Weight and Mass");
    weightAndMass["w"] = weightPerUnitVolume;
    weightAndMass["m"] = massPerUnitVolume;
  }

  private void GetMechanicalProperties(string materialName, Dictionary<string, object?> properties)
  {
    int materialDirectionalSymmetryKey = 0;
    eMatType materialType = default;

    _settingsStore.Current.SapModel.PropMaterial.GetTypeOAPI(
      materialName,
      ref materialType,
      ref materialDirectionalSymmetryKey
    );

    var materialDirectionalSymmetryValue = materialDirectionalSymmetryKey switch
    {
      0 => DirectionalSymmetryType.ISOTROPIC,
      1 => DirectionalSymmetryType.ORTHOTROPIC,
      2 => DirectionalSymmetryType.ANISOTROPIC,
      3 => DirectionalSymmetryType.UNIAXIAL,
      _ => throw new ArgumentException($"Unknown symmetry type: {materialDirectionalSymmetryKey}")
    };

    var mechanicalProperties = DictionaryUtils.EnsureNestedDictionary(properties, "Mechanical Properties");
    mechanicalProperties["directionalSymmetryType"] = materialDirectionalSymmetryValue.ToString();

    GetMechanicalPropertiesByType(materialName, materialDirectionalSymmetryValue, mechanicalProperties);
  }

  private void GetMechanicalPropertiesByType(
    string materialName,
    DirectionalSymmetryType directionalSymmetryType,
    Dictionary<string, object?> mechanicalProperties
  )
  {
    switch (directionalSymmetryType)
    {
      case DirectionalSymmetryType.ISOTROPIC:
        ExtractIsotropicProperties(materialName, mechanicalProperties);
        break;
      case DirectionalSymmetryType.ORTHOTROPIC:
        ExtractOrthotropicProperties(materialName, mechanicalProperties);
        break;
      case DirectionalSymmetryType.ANISOTROPIC:
        ExtractAnisotropicProperties(materialName, mechanicalProperties);
        break;
      case DirectionalSymmetryType.UNIAXIAL:
        ExtractUniaxialProperties(materialName, mechanicalProperties);
        break;
      default:
        throw new ArgumentException($"Unknown directional symmetry type: {directionalSymmetryType}");
    }
  }

  private void ExtractIsotropicProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double modulusOfElasticity = double.NaN;
    double poissonRatio = double.NaN;
    double thermalCoefficient = double.NaN;
    double shearModulus = double.NaN;

    _settingsStore.Current.SapModel.PropMaterial.GetMPIsotropic(
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

    _settingsStore.Current.SapModel.PropMaterial.GetMPOrthotropic(
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

    _settingsStore.Current.SapModel.PropMaterial.GetMPAnisotropic(
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

    _settingsStore.Current.SapModel.PropMaterial.GetMPUniaxial(
      materialName,
      ref modulusOfElasticity,
      ref thermalCoefficient
    );

    mechanicalProperties["modulusOfElasticity"] = modulusOfElasticity;
    mechanicalProperties["thermalCoefficient"] = thermalCoefficient;
  }
}
