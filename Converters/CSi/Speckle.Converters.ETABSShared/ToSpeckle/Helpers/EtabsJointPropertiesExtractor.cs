using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from joint elements using the PointObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Extracts properties only available in ETABS (e.g., Diaphragm)
/// - Complements <see cref="CsiJointPropertiesExtractor"/> by adding product-specific data
/// - Follows same pattern of single-purpose methods for clear API mapping
///
/// Design Decisions:
/// - Maintains separate methods for each property following CSI API structure
/// - Properties are organized by their functional groups (Object ID, Assignments, Design)
///
/// Integration:
/// - Used by <see cref="EtabsPropertiesExtractor"/> for joint-specific property extraction
/// - Works alongside CsiJointPropertiesExtractor to build complete property set
/// </remarks>
public sealed class EtabsJointPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsJointPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiJointWrapper joint, Dictionary<string, object?> properties)
  {
    var objectId = DictionaryUtils.EnsureNestedDictionary(properties, "Object ID");
    (objectId["label"], objectId["level"]) = GetLabelAndLevel(joint);

    var assignments = DictionaryUtils.EnsureNestedDictionary(properties, "Assignments");
    (assignments["diaphragmOption"], assignments["diaphragmName"]) = GetAssignedDiaphragm(joint);
    assignments["springAssignment"] = GetSpringAssignmentName(joint);
  }

  private (string diaphramOption, string diaphragmName) GetAssignedDiaphragm(CsiJointWrapper joint)
  {
    eDiaphragmOption diaphragmOption = eDiaphragmOption.Disconnect;
    string diaphragmName = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.PointObj.GetDiaphragm(joint.Name, ref diaphragmOption, ref diaphragmName);
    return (diaphragmOption.ToString(), diaphragmName);
  }

  private (string label, string level) GetLabelAndLevel(CsiJointWrapper joint)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.PointObj.GetLabelFromName(joint.Name, ref label, ref level);
    return (label, level);
  }

  private string GetSpringAssignmentName(CsiJointWrapper joint)
  {
    string springPropertyName = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.PointObj.GetSpringAssignment(joint.Name, ref springPropertyName);
    return springPropertyName;
  }
}
