using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to joint elements across CSi products (e.g., ETABS, SAP2000)
/// using the PointObj API calls.
/// </summary>
/// <remarks>
/// Design Decisions:
/// <list type="bullet">
///     <item>
///         <description>
///             Individual methods preferred over batched calls due to:
///             <list type="bullet">
///                 <item><description>Independent API calls with no performance gain from batching (?)</description></item>
///                 <item><description>Easier debugging and error tracing</description></item>
///                 <item><description>Simpler maintenance as each method maps to one API concept</description></item>
///             </list>
///         </description>
///     </item>
/// </list>
///
/// Responsibilities:
/// <list type="bullet">
///     <item><description>Provides a focused interface for extracting properties specific to joint elements.</description></item>
///     <item><description>Ensures consistency in property extraction logic across supported CSi products.</description></item>
/// </list>
/// </remarks>
public sealed class CsiJointPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiJointPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiJointWrapper joint, PropertyExtractionResult jointData)
  {
    jointData.ApplicationId = joint.GetSpeckleApplicationId(_settingsStore.Current.SapModel);

    var assignments = jointData.Properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    assignments[CommonObjectProperty.GROUPS] = new List<string>(GetGroupAssigns(joint));
    assignments["Restraints"] = GetRestraints(joint);
  }

  private string[] GetGroupAssigns(CsiJointWrapper joint)
  {
    int numberGroups = 0;
    string[] groups = [];
    _ = _settingsStore.Current.SapModel.PointObj.GetGroupAssign(joint.Name, ref numberGroups, ref groups);
    return (groups);
  }

  private Dictionary<string, bool?> GetRestraints(CsiJointWrapper joint)
  {
    bool[] restraints = [];
    _ = _settingsStore.Current.SapModel.PointObj.GetRestraint(joint.Name, ref restraints);
    return new Dictionary<string, bool?>
    {
      ["UX Restrained"] = restraints[0],
      ["UY Restrained"] = restraints[1],
      ["UZ Restrained"] = restraints[2],
      ["RX Restrained"] = restraints[3],
      ["RY Restrained"] = restraints[4],
      ["RZ Restrained"] = restraints[5],
    };
  }
}
