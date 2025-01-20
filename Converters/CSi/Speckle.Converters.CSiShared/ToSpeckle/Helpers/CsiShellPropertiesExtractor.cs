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
/// - Used by <see cref="SharedPropertiesExtractor"/> for delegating shell property extraction
/// </remarks>
public sealed class CsiShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiShellPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiShellWrapper shell, PropertyExtractionResult shellData)
  {
    shellData.ApplicationId = shell.GetSpeckleApplicationId(_settingsStore.Current.SapModel);

    var geometry = DictionaryUtils.EnsureNestedDictionary(shellData.Properties, "Geometry");
    geometry["shellVerticesJointNames"] = GetPointNames(shell);

    var assignments = DictionaryUtils.EnsureNestedDictionary(shellData.Properties, "Assignments");
    assignments["groups"] = new List<string>(GetGroupAssigns(shell));
    assignments["localAxis"] = GetLocalAxes(shell);
    assignments["materialOverwrite"] = GetMaterialOverwrite(shell);
    assignments["propertyModifiers"] = GetModifiers(shell);
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
      ["f11"] = value[0],
      ["f22"] = value[1],
      ["f12"] = value[2],
      ["m11"] = value[3],
      ["m22"] = value[4],
      ["m12"] = value[5],
      ["v13"] = value[6],
      ["v23"] = value[7],
      ["mass"] = value[8],
      ["weight"] = value[9]
    };
  }

  private string[] GetPointNames(CsiShellWrapper shell)
  {
    int numberPoints = 0;
    string[] pointNames = Array.Empty<string>();
    _ = _settingsStore.Current.SapModel.AreaObj.GetPoints(shell.Name, ref numberPoints, ref pointNames);
    return pointNames;
  }
}
