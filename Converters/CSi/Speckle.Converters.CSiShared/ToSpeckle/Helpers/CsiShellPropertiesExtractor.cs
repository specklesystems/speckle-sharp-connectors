using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to shell elements across CSi products (e.g., Etabs, Sap2000)
/// using the AreaObj API calls.
/// </summary>
/// <remarks>
/// Design Decisions:
/// - Individual methods preferred over batched calls due to:
///   * Independent API calls with no performance gain from batching (?)
///   * Easier debugging and error tracing
///   * Simpler maintenance as each method maps to one API concept
/// Integration:
/// - Part of the property extraction hierarchy
/// - Used by <see cref="CsiGeneralPropertiesExtractor"/> for delegating shell property extraction
/// </remarks>
public sealed class CsiShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiShellPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiShellWrapper shell, Dictionary<string, object?> properties)
  {
    properties["applicationId"] = GetApplicationId(shell);

    var geometry = DictionaryUtils.EnsureNestedDictionary(properties, "Geometry");
    geometry["shellVerticesJointNames"] = GetPointNames(shell);

    var assignments = DictionaryUtils.EnsureNestedDictionary(properties, "Assignments");
    assignments["groups"] = new List<string>(GetGroupAssigns(shell));
    assignments["localAxis"] = GetLocalAxes(shell);
    assignments["materialOverwrite"] = GetMaterialOverwrite(shell);
    assignments["propertyModifiers"] = GetModifiers(shell);
    assignments["sectionProperty"] = GetSectionName(shell);
  }

  private string GetApplicationId(CsiShellWrapper shell) =>
    shell.GetSpeckleApplicationId(_settingsStore.Current.SapModel);

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
    return new Dictionary<string, object?> { ["angle"] = angle, ["advanced"] = advanced.ToString() };
  }

  private string GetMaterialOverwrite(CsiShellWrapper shell)
  {
    string propName = "None";
    _ = _settingsStore.Current.SapModel.AreaObj.GetMaterialOverwrite(shell.Name, ref propName);
    return propName;
  }

  private Dictionary<string, double?> GetModifiers(CsiShellWrapper shell)
  {
    double[] value = Array.Empty<double>();
    _ = _settingsStore.Current.SapModel.AreaObj.GetModifiers(shell.Name, ref value);
    return new Dictionary<string, double?>
    {
      ["membraneF11Modifier"] = value[0],
      ["membraneF22Modifier"] = value[1],
      ["membraneF12Modifier"] = value[2],
      ["bendingM11Modifier"] = value[3],
      ["bendingM22Modifier"] = value[4],
      ["bendingM12Modifier"] = value[5],
      ["shearV13Modifier"] = value[6],
      ["shearV23Modifier"] = value[7],
      ["massModifier"] = value[8],
      ["weightModifier"] = value[9]
    };
  }

  private string[] GetPointNames(CsiShellWrapper shell)
  {
    int numberPoints = 0;
    string[] pointNames = Array.Empty<string>();
    _ = _settingsStore.Current.SapModel.AreaObj.GetPoints(shell.Name, ref numberPoints, ref pointNames);
    return pointNames;
  }

  private string GetSectionName(CsiShellWrapper shell)
  {
    string sectionName = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetProperty(shell.Name, ref sectionName);
    return sectionName;
  }
}
