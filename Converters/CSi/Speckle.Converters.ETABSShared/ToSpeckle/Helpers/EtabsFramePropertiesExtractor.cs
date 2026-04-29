using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from frame elements using the FrameObj API calls.
/// </summary>
public sealed class EtabsFramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsFramePropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiFrameWrapper frame, Dictionary<string, object?> properties)
  {
    var objectId = properties.EnsureNested(ObjectPropertyCategory.OBJECT_ID);
    objectId[CommonObjectProperty.DESIGN_ORIENTATION] = GetDesignOrientation(frame);
    (objectId[CommonObjectProperty.LABEL], objectId[CommonObjectProperty.LEVEL]) = GetLabelAndLevel(frame);

    var assignments = properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    assignments[CommonObjectProperty.SPRING_ASSIGNMENT] = GetSpringAssignmentName(frame);

    var design = properties.EnsureNested(ObjectPropertyCategory.DESIGN);
    design[ObjectPropertyKey.DESIGN_PROCEDURE] = GetDesignProcedure(frame);
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
      _ => "Program determined",
    };
  }

  private string GetSpringAssignmentName(CsiFrameWrapper frame)
  {
    string springPropertyName = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetSpringAssignment(frame.Name, ref springPropertyName);
    return springPropertyName;
  }
}
