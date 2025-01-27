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

    var geometry = DictionaryUtils.EnsureNestedDictionary(shellData.Properties, ObjectPropertyCategory.GEOMETRY);
    geometry["Joints"] = GetPointNames(shell);

    var assignments = DictionaryUtils.EnsureNestedDictionary(shellData.Properties, ObjectPropertyCategory.ASSIGNMENTS);
    assignments["Groups"] = new List<string>(GetGroupAssigns(shell));
    assignments["Local Axis 2 Angle"] = GetLocalAxes(shell);
    assignments["Material Overwrite"] = GetMaterialOverwrite(shell);
    assignments["Property Modifiers"] = GetModifiers(shell);
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

    return new Dictionary<string, object?>
    {
      ["Angle"] = DictionaryUtils.CreateValueUnitDictionary("Angle", angle, "Degrees"),
      ["Advanced"] = advanced.ToString()
    };
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
