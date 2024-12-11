using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from shell elements using the AreaObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Extracts properties only available in ETABS (e.g., Label, Level)
/// - Complements <see cref="CsiShellPropertiesExtractor"/> by adding product-specific data
/// - Follows same pattern of single-purpose methods for clear API mapping
///
/// Design Decisions:
/// - Maintains separate methods for each property following CSI API structure
/// - Properties are organized by their functional groups (Object ID, Assignments, Design)
///
/// Integration:
/// - Used by <see cref="EtabsPropertiesExtractor"/> for shell-specific property extraction
/// - Works alongside CsiShellPropertiesExtractor to build complete property set
/// </remarks>
public sealed class EtabsShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsShellPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiShellWrapper shell, Dictionary<string, object?> properties)
  {
    var objectId = DictionaryUtils.EnsureNestedDictionary(properties, "Object ID");
    objectId["designOrientation"] = GetDesignOrientation(shell);
    (objectId["label"], objectId["level"]) = GetLabelAndLevel(shell);

    var assignments = DictionaryUtils.EnsureNestedDictionary(properties, "Assignments");
    assignments["diaphragmName"] = GetAssignedDiaphragmName(shell);
    assignments["isOpening"] = IsOpening(shell);
    assignments["pierAssignment"] = GetPierAssignmentName(shell);
    assignments["spandrelAssignment"] = GetSpandrelAssignmentName(shell);
    assignments["springAssignmentName"] = GetSpringAssignmentName(shell);
  }

  private (string label, string level) GetLabelAndLevel(CsiShellWrapper shell)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetLabelFromName(shell.Name, ref label, ref level);
    return (label, level);
  }

  private string GetDesignOrientation(CsiShellWrapper shell)
  {
    eAreaDesignOrientation designOrientation = eAreaDesignOrientation.Null;
    _ = _settingsStore.Current.SapModel.AreaObj.GetDesignOrientation(shell.Name, ref designOrientation);
    return designOrientation.ToString();
  }

  private string GetAssignedDiaphragmName(CsiShellWrapper shell)
  {
    string diaphragmName = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.AreaObj.GetDiaphragm(shell.Name, ref diaphragmName);
    return diaphragmName;
  }

  private string IsOpening(CsiShellWrapper shell)
  {
    bool isOpening = false;
    _ = _settingsStore.Current.SapModel.AreaObj.GetOpening(shell.Name, ref isOpening);
    return isOpening.ToString();
  }

  private string GetPierAssignmentName(CsiShellWrapper shell)
  {
    string pierAssignment = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.AreaObj.GetPier(shell.Name, ref pierAssignment);
    return pierAssignment;
  }

  private string GetSpandrelAssignmentName(CsiShellWrapper shell)
  {
    string spandrelAssignment = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.AreaObj.GetSpandrel(shell.Name, ref spandrelAssignment);
    return spandrelAssignment;
  }

  private string GetSpringAssignmentName(CsiShellWrapper shell)
  {
    string springAssignmentName = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.AreaObj.GetSpringAssignment(shell.Name, ref springAssignmentName);
    return springAssignmentName;
  }
}
