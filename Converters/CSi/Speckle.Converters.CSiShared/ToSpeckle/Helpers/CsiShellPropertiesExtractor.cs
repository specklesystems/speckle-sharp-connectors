using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to shell elements across CSi products (e.g., Etabs, Sap2000)
/// using the AreaObj API calls.
/// </summary>
public sealed class CsiShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  public CsiShellPropertiesExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton
  )
  {
    _settingsStore = settingsStore;
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
  }

  public void ExtractProperties(CsiShellWrapper shell, PropertyExtractionResult shellData)
  {
    shellData.ApplicationId = shell.GetSpeckleApplicationId(_settingsStore.Current.SapModel);

    var geometry = shellData.Properties.EnsureNested(ObjectPropertyCategory.GEOMETRY);
    geometry["Joints"] = GetPointNames(shell);

    var assignments = shellData.Properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    assignments[CommonObjectProperty.GROUPS] = GetGroupAssigns(shell);
    assignments[CommonObjectProperty.LOCAL_AXIS_2_ANGLE] = GetLocalAxes(shell);
    assignments[CommonObjectProperty.MATERIAL_OVERWRITE] = GetMaterialOverwrite(shell);
    assignments[CommonObjectProperty.PROPERTY_MODIFIERS] = GetModifiers(shell);
  }

  private string[] GetGroupAssigns(CsiShellWrapper shell)
  {
    int numberGroups = 0;
    string[] groups = [];
    _ = _settingsStore.Current.SapModel.AreaObj.GetGroupAssign(shell.Name, ref numberGroups, ref groups);
    return (groups);
  }

  private Dictionary<string, object?> GetLocalAxes(CsiShellWrapper shell)
  {
    double angle = 0;
    bool advanced = false;
    _ = _settingsStore.Current.SapModel.AreaObj.GetLocalAxes(shell.Name, ref angle, ref advanced);

    Dictionary<string, object?> resultsDictionary = [];
    resultsDictionary.AddWithUnits(CommonObjectProperty.ANGLE, angle, "Degrees");
    resultsDictionary[CommonObjectProperty.ADVANCED] = advanced.ToString();

    return resultsDictionary;
  }

  private string GetMaterialOverwrite(CsiShellWrapper shell)
  {
    string propName = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetMaterialOverwrite(shell.Name, ref propName);
    return propName;
  }

  private Dictionary<string, double?> GetModifiers(CsiShellWrapper shell)
  {
    double[] value = [];
    _ = _settingsStore.Current.SapModel.AreaObj.GetModifiers(shell.Name, ref value);
    return new Dictionary<string, double?>
    {
      ["Membrane F11 Modifier"] = value[0],
      ["Membrane F22 Modifier"] = value[1],
      ["Membrane F12 Modifier"] = value[2],
      ["Bending M11 Modifier"] = value[3],
      ["Bending M22 Modifier"] = value[4],
      ["Bending M12 Modifier"] = value[5],
      ["Shear V13 Modifier"] = value[6],
      ["Shear V23 Modifier"] = value[7],
      ["Mass"] = value[8],
      ["Weight"] = value[9]
    };
  }

  private string[] GetPointNames(CsiShellWrapper shell)
  {
    int numberPoints = 0;
    string[] pointNames = [];
    _ = _settingsStore.Current.SapModel.AreaObj.GetPoints(shell.Name, ref numberPoints, ref pointNames);
    return pointNames;
  }
}
