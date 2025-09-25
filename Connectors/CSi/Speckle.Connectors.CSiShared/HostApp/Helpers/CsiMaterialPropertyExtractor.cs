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
public class CsiMaterialPropertyExtractor : IMaterialPropertyExtractor
{
  /// <summary>
  /// Property strings for all mechanical properties, used by numerous methods.
  /// </summary>
  private static class MechanicalPropertyNames
  {
    public const string MODULUS_OF_ELASTICITY = "Modulus of Elasticity, E";
    public const string MODULUS_OF_ELASTICITY_ARRAY = "Modulus of Elasticity Array, E";
    public const string POISSON_RATIO = "Poisson's Ratio, U";
    public const string POISSON_RATIO_ARRAY = "Poisson's Ratio Array, U";
    public const string THERMAL_COEFFICIENT = "Coefficient of Thermal Expansion, A";
    public const string THERMAL_COEFFICIENT_ARRAY = "Coefficient of Thermal Expansion Array, A";
    public const string SHEAR_MODULUS = "Shear Modulus, G";
    public const string SHEAR_MODULUS_ARRAY = "Shear Modulus Array, G";
  }

  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiMaterialPropertyExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(string name, Dictionary<string, object?> properties)
  {
    GetGeneralProperties(name, properties);
    GetWeightAndMassProperties(name, properties);
    GetMechanicalProperties(name, properties);
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

      var generalData = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
      generalData["Name"] = materialName;
      generalData["Type"] = materialType.ToString();
      generalData["Notes"] = materialNotes;
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

    var weightAndMass = properties.EnsureNested(SectionPropertyCategory.WEIGHT_AND_MASS);
    weightAndMass["Weight per Unit Volume"] = weightPerUnitVolume;
    weightAndMass["Mass per Unit Volume"] = massPerUnitVolume;
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

    var mechanicalProperties = properties.EnsureNested(SectionPropertyCategory.MECHANICAL_DATA);
    mechanicalProperties["Directional Symmetry Type"] = materialDirectionalSymmetryValue.ToString();

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

    mechanicalProperties[MechanicalPropertyNames.MODULUS_OF_ELASTICITY] = modulusOfElasticity;
    mechanicalProperties[MechanicalPropertyNames.POISSON_RATIO] = poissonRatio;
    mechanicalProperties[MechanicalPropertyNames.THERMAL_COEFFICIENT] = thermalCoefficient;
    mechanicalProperties[MechanicalPropertyNames.SHEAR_MODULUS] = shearModulus;
  }

  private void ExtractOrthotropicProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double[] modulusOfElasticityArray = [];
    double[] poissonRatioArray = [];
    double[] thermalCoefficientArray = [];
    double[] shearModulusArray = [];

    _settingsStore.Current.SapModel.PropMaterial.GetMPOrthotropic(
      materialName,
      ref modulusOfElasticityArray,
      ref poissonRatioArray,
      ref thermalCoefficientArray,
      ref shearModulusArray
    );

    mechanicalProperties[MechanicalPropertyNames.MODULUS_OF_ELASTICITY_ARRAY] = modulusOfElasticityArray;
    mechanicalProperties[MechanicalPropertyNames.POISSON_RATIO_ARRAY] = poissonRatioArray;
    mechanicalProperties[MechanicalPropertyNames.THERMAL_COEFFICIENT_ARRAY] = thermalCoefficientArray;
    mechanicalProperties[MechanicalPropertyNames.SHEAR_MODULUS_ARRAY] = shearModulusArray;
  }

  private void ExtractAnisotropicProperties(string materialName, Dictionary<string, object?> mechanicalProperties)
  {
    double[] modulusOfElasticityArray = [];
    double[] poissonRatioArray = [];
    double[] thermalCoefficientArray = [];
    double[] shearModulusArray = [];

    _settingsStore.Current.SapModel.PropMaterial.GetMPAnisotropic(
      materialName,
      ref modulusOfElasticityArray,
      ref poissonRatioArray,
      ref thermalCoefficientArray,
      ref shearModulusArray
    );

    mechanicalProperties[MechanicalPropertyNames.MODULUS_OF_ELASTICITY_ARRAY] = modulusOfElasticityArray;
    mechanicalProperties[MechanicalPropertyNames.POISSON_RATIO_ARRAY] = poissonRatioArray;
    mechanicalProperties[MechanicalPropertyNames.THERMAL_COEFFICIENT_ARRAY] = thermalCoefficientArray;
    mechanicalProperties[MechanicalPropertyNames.SHEAR_MODULUS_ARRAY] = shearModulusArray;
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

    mechanicalProperties[MechanicalPropertyNames.MODULUS_OF_ELASTICITY] = modulusOfElasticity;
    mechanicalProperties[MechanicalPropertyNames.THERMAL_COEFFICIENT] = thermalCoefficient;
  }
}
