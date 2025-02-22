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
/// <list type="bullet">
///     <item><description>Extracts properties only available in ETABS (e.g., Diaphragm)</description></item>
///     <item><description>Complements <see cref="CsiJointPropertiesExtractor"/> by adding product-specific data</description></item>
///     <item><description>Follows same pattern of single-purpose methods for clear API mapping</description></item>
/// </list>
///
/// Design Decisions:
/// <list type="bullet">
///     <item><description>Maintains separate methods for each property following CSI API structure</description></item>
///     <item><description>Properties are organized by their functional groups (Object ID, Assignments, Design)</description></item>
/// </list>
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
    var objectId = properties.EnsureNested(ObjectPropertyCategory.OBJECT_ID);
    (objectId[CommonObjectProperty.LABEL], objectId[CommonObjectProperty.LEVEL]) = GetLabelAndLevel(joint);

    var assignments = properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    (assignments["Diaphragm Option"], assignments["Diaphragm Name"]) = GetAssignedDiaphragm(joint);
    assignments[CommonObjectProperty.SPRING_ASSIGNMENT] = GetSpringAssignmentName(joint);
  }

  private (string diaphramOption, string diaphragmName) GetAssignedDiaphragm(CsiJointWrapper joint)
  {
    eDiaphragmOption diaphragmOption = eDiaphragmOption.Disconnect;
    string diaphragmName = string.Empty;
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
    string springPropertyName = string.Empty;
    _ = _settingsStore.Current.SapModel.PointObj.GetSpringAssignment(joint.Name, ref springPropertyName);
    return springPropertyName;
  }
}
