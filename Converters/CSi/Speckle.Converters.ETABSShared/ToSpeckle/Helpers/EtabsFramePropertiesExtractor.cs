using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from frame elements using the FrameObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Extracts properties only available in ETABS (e.g., Label, Level)
/// - Complements <see cref="CsiFramePropertiesExtractor"/> by adding product-specific data
/// - Follows same pattern of single-purpose methods for clear API mapping
///
/// Design Decisions:
/// - Maintains separate methods for each property following CSI API structure
/// - Properties are organized by their functional groups (Object ID, Assignments, Design)
///
/// Integration:
/// - Used by <see cref="EtabsPropertiesExtractor"/> for frame-specific property extraction
/// - Works alongside CsiFramePropertiesExtractor to build complete property set
/// </remarks>
public sealed class EtabsFramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsFramePropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiFrameWrapper frame, Dictionary<string, object?> properties)
  {
    var objectId = DictionaryUtils.EnsureNestedDictionary(properties, ObjectPropertyCategory.OBJECT_ID);
    objectId["designOrientation"] = GetDesignOrientation(frame);
    (objectId["label"], objectId["level"]) = GetLabelAndLevel(frame);

    var assignments = DictionaryUtils.EnsureNestedDictionary(properties, ObjectPropertyCategory.ASSIGNMENTS);
    assignments["springAssignment"] = GetSpringAssignmentName(frame);

    var design = DictionaryUtils.EnsureNestedDictionary(properties, ObjectPropertyCategory.DESIGN);
    design["designProcedure"] = GetDesignProcedure(frame);
  }

  private (string label, string level) GetLabelAndLevel(CsiFrameWrapper frame)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetLabelFromName(frame.Name, ref label, ref level);
    return (label, level);
  }

  private string GetDesignOrientation(CsiFrameWrapper frame)
  {
    eFrameDesignOrientation designOrientation = eFrameDesignOrientation.Null;
    _ = _settingsStore.Current.SapModel.FrameObj.GetDesignOrientation(frame.Name, ref designOrientation);
    return designOrientation.ToString();
  }

  private string GetDesignProcedure(CsiFrameWrapper frame)
  {
    int myType = 0;
    _ = _settingsStore.Current.SapModel.FrameObj.GetDesignProcedure(frame.Name, ref myType);
    return myType switch
    {
      1 => "Steel Frame Design",
      2 => "Concrete Frame Design",
      3 => "Composite Beam Design",
      4 => "Steel Joist Design",
      7 => "No Design",
      13 => "Composite Column Design",
      _ => "Program determined"
    };
  }

  private string GetSpringAssignmentName(CsiFrameWrapper frame)
  {
    string springPropertyName = "None"; // Is there a better way to handle null?
    _ = _settingsStore.Current.SapModel.FrameObj.GetSpringAssignment(frame.Name, ref springPropertyName);
    return springPropertyName;
  }
}
