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
  private readonly DatabaseTableExtractor _databaseTableExtractor;

  public EtabsFramePropertiesExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DatabaseTableExtractor databaseTableExtractor
  )
  {
    _settingsStore = settingsStore;
    _databaseTableExtractor = databaseTableExtractor;
  }

  public void ExtractProperties(CsiFrameWrapper frame, Dictionary<string, object?> properties)
  {
    var objectId = properties.EnsureNested(ObjectPropertyCategory.OBJECT_ID);
    objectId[CommonObjectProperty.DESIGN_ORIENTATION] = GetDesignOrientation(frame);
    (objectId[CommonObjectProperty.LABEL], objectId[CommonObjectProperty.LEVEL]) = GetLabelAndLevel(frame);

    var assignments = properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    assignments[CommonObjectProperty.SPRING_ASSIGNMENT] = GetSpringAssignmentName(frame);

    var design = properties.EnsureNested(ObjectPropertyCategory.DESIGN);
    design["Design Procedure"] = GetDesignProcedure(frame);

    var geometry = properties.EnsureNested(ObjectPropertyCategory.GEOMETRY);
    double length = GetLength(frame);
    geometry.AddWithUnits("Length", length, _settingsStore.Current.SpeckleUnits);
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
    string springPropertyName = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetSpringAssignment(frame.Name, ref springPropertyName);
    return springPropertyName;
  }

  private double GetLength(CsiFrameWrapper frame)
  {
    // using the DatabaseTableExtractor fetch table with key "Frame Assignments - Summary"
    // limit query size to "UniqueName" and "Length" fields
    var frameLengthData = _databaseTableExtractor
      .GetTableData("Frame Assignments - Summary", ["UniqueName", "Length"])
      .Rows[frame.Name];

    // all database data is returned as strings
    return double.TryParse(frameLengthData["Length"], out double result) ? result : double.NaN;
  }
}
