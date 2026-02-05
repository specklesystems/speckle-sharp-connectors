using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.ETABS22.HostApp.Helpers;

public class EtabsMaterialPropertyExtractor : IApplicationMaterialPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  private readonly Dictionary<int, string?> _ssTypeDict =
    new()
    {
      { 0, "User defined" },
      { 1, "Parametric - Simple" },
      { 2, "Parametric - Mander" },
    };

  private readonly Dictionary<int, string?> _ssHysTypeDict =
    new()
    {
      { 0, "Elastic" },
      { 1, "Kinematic" },
      { 2, "Takeda" },
      { 3, "Pivot" },
      { 4, "Concrete" },
      { 5, "BRB Hardening" },
      { 6, "Degrading" },
      { 7, "Isotropic" },
    };

  private const int TEMP = 0;

  public EtabsMaterialPropertyExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(string name, Dictionary<string, object?> properties)
  {
    // we want to get some of the "other" material property data that is type specific
    // csi extractor populates "type" string, but api query arguably simpler and more reliable than dict string access
    int symType = 0;
    eMatType matType = 0;
    _settingsStore.Current.SapModel.PropMaterial.GetTypeOAPI(name, ref matType, ref symType);

    // we don't have design data api queries for these, so early return to avoid creating that specific dictionary
    if (matType is eMatType.NoDesign or eMatType.Aluminum or eMatType.ColdFormed or eMatType.Masonry)
    {
      return;
    }

    // ensure design data specific properties dictionary that will be mutated in switch expression
    var designData = properties.EnsureNested(SectionPropertyCategory.DESIGN_DATA);

    // can't do a switch expression here because not all enums have an api query (e.g. masonry, aluminium)
    switch (matType)
    {
      case eMatType.Steel:
        ExtractSteelProperties(name, designData);
        break;
      case eMatType.Concrete:
        ExtractConcreteProperties(name, designData);
        break;
      case eMatType.Rebar:
        ExtractRebarProperties(name, designData);
        break;
      case eMatType.Tendon:
        ExtractTendonProperties(name, designData);
        break;
    }
  }

  private void ExtractSteelProperties(string name, Dictionary<string, object?> designData)
  {
    // step 1: stubs for api query
    int ssType = 0,
      ssHysType = 0;
    double fy = 0,
      fu = 0,
      eFy = 0,
      eFu = 0,
      strainAtHardening = 0,
      strainAtMaxStress = 0,
      strainAtRupture = 0;

    // step 2: api query
    // NOTE: using the "older" method. Not sure if _1 is unsupported in etabs 21
    // also, _1 doesn't give a lot more MEANINGFUL data
    _settingsStore.Current.SapModel.PropMaterial.GetOSteel(
      name,
      ref fy,
      ref fu,
      ref eFy,
      ref eFu,
      ref ssType,
      ref ssHysType,
      ref strainAtHardening,
      ref strainAtMaxStress,
      ref strainAtRupture
    );

    // step 3: mutate properties dictionary
    designData["Fy"] = fy;
    designData["Fu"] = fu;
    designData["EFy"] = eFy;
    designData["EFu"] = eFu;
    designData["SSType"] = _ssTypeDict.TryGetValue(ssType, out string? ssTypeValue) ? ssTypeValue : "";
    designData["SSHysType"] = _ssHysTypeDict.TryGetValue(ssHysType, out string? ssHysTypeValue) ? ssHysTypeValue : "";
    designData["StrainAtHardening"] = strainAtHardening;
    designData["StrainAtMaxStress"] = strainAtMaxStress;
    designData["StrainAtRupture"] = strainAtRupture;
    designData["Temp"] = TEMP;
  }

  private void ExtractConcreteProperties(string name, Dictionary<string, object?> designData)
  {
    // step 1: stubs for api query
    int ssType = 0,
      ssHysType = 0;
    bool isLightweight = false;
    double fc = 0,
      fcsFactor = 0,
      strainAtFc = 0,
      strainUltimate = 0,
      frictionAngle = 0,
      dilatationalAngle = 0;

    // step 2: api query
    // NOTE: using the "older" method. Not sure if _1 or _2 are unsupported in etabs 21
    // also, _1 or _2 doesn't give a lot more MEANINGFUL data
    _settingsStore.Current.SapModel.PropMaterial.GetOConcrete(
      name,
      ref fc,
      ref isLightweight,
      ref fcsFactor,
      ref ssType,
      ref ssHysType,
      ref strainAtFc,
      ref strainUltimate,
      ref frictionAngle,
      ref dilatationalAngle
    );

    // step 3: mutate properties dictionary
    designData["Fc"] = fc;
    designData["FcsFactor"] = fcsFactor;
    designData["StrainAtFc"] = strainAtFc;
    designData["StrainUltimate"] = strainUltimate;
    designData["FrictionAngle"] = frictionAngle;
    designData["DilatationalAngle"] = dilatationalAngle;
    designData["IsLightweight"] = isLightweight.ToString();
    designData["SSType"] = _ssTypeDict.TryGetValue(ssType, out string? ssTypeValue) ? ssTypeValue : "";
    designData["SSHysType"] = _ssHysTypeDict.TryGetValue(ssHysType, out string? ssHysTypeValue) ? ssHysTypeValue : "";
    designData["Temp"] = TEMP;
  }

  private void ExtractRebarProperties(string name, Dictionary<string, object?> designData)
  {
    // step 1: stubs for api query
    bool useCaltransSsDefaults = false;
    int ssType = 0,
      ssHysType = 0;
    double fy = 0,
      fu = 0,
      eFy = 0,
      eFu = 0,
      strainAtHardening = 0,
      strainUltimate = 0;

    // step 2: api query
    // NOTE: using the "older" method. Not sure if _1 is unsupported in etabs 21
    // also, _1 doesn't give a lot more MEANINGFUL data
    _settingsStore.Current.SapModel.PropMaterial.GetORebar(
      name,
      ref fy,
      ref fu,
      ref eFy,
      ref eFu,
      ref ssType,
      ref ssHysType,
      ref strainAtHardening,
      ref strainUltimate,
      ref useCaltransSsDefaults
    );

    // step 3: mutate properties dictionary
    designData["Fy"] = fy;
    designData["Fu"] = fu;
    designData["EFy"] = eFy;
    designData["EFu"] = eFu;
    designData["StrainAtHardening"] = strainAtHardening;
    designData["StrainUltimate"] = strainUltimate;
    designData["SSType"] = _ssTypeDict.TryGetValue(ssType, out string? ssTypeValue) ? ssTypeValue : "";
    designData["SSHysType"] = _ssHysTypeDict.TryGetValue(ssHysType, out string? ssHysTypeValue) ? ssHysTypeValue : "";
    designData["UseCaltransSsDefaults"] = useCaltransSsDefaults.ToString();
    designData["Temp"] = TEMP;
  }

  private void ExtractTendonProperties(string name, Dictionary<string, object?> designData)
  {
    // step 1: stubs for api query
    int ssType = 0,
      ssHysType = 0;
    double fy = 0,
      fu = 0;

    // step 2: api query
    _settingsStore.Current.SapModel.PropMaterial.GetOTendon(name, ref fy, ref fu, ref ssType, ref ssHysType);

    // step 3: mutate properties dictionary
    designData["Fy"] = fy;
    designData["Fu"] = fu;
    designData["SSType"] = _ssTypeDict.TryGetValue(ssType, out string? ssTypeValue) ? ssTypeValue : "";
    designData["SSHysType"] = _ssHysTypeDict.TryGetValue(ssHysType, out string? ssHysTypeValue) ? ssHysTypeValue : "";
    designData["Temp"] = TEMP;
  }
}
