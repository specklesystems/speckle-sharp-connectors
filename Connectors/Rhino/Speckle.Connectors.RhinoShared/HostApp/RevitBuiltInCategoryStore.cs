// TODO: Where should this live? Speckle.Connectors.Common.Interop??

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Represents a category option for the dropdown in the UI.
/// </summary>
/// <param name="Value">The Revit category enum name (e.g., "OST_Walls")</param>
/// <param name="Label">Human-readable name for the UI (e.g., "Walls")</param>
public record CategoryOption(string Value, string Label);

/// <summary>
/// Currently hardcoded Revit BuiltInCategories for the Interop Lite mapper.
/// This provides a select list of commonly used categories for object mapping
/// from Rhino to Revit.
///
/// NOTE: Currently located in Rhino codebase for Rhino-to-Revit POC use case.
/// When we extend to other connectors implementing a RevitMapper interface,
/// this should be moved to a shared location (e.g., Speckle.Connectors.Common
/// or Speckle.Converters.RevitShared).
/// </summary>
public static class RevitBuiltInCategoryStore
{
  /// <summary>
  /// Dictionary mapping Revit BuiltInCategory enum names to human-readable labels.
  /// Key: BuiltInCategory enum name (e.g., "OST_Walls")
  /// Value: Human-readable label (e.g., "Walls")
  /// </summary>
  public static readonly CategoryOption[] Categories =
  [
    new("OST_Ceilings", "Ceilings"),
    new("OST_Columns", "Columns"),
    new("OST_CurtainGrids", "Curtain Grids"),
    new("OST_CurtainGridsCurtaSystem", "Curtain Grids - Curtain System"),
    new("OST_CurtainGridsRoof", "Curtain Grids - Roof"),
    new("OST_CurtainGridsSystem", "Curtain Grids - System"),
    new("OST_CurtainGridsWall", "Curtain Grids - Wall"),
    new("OST_Curtain_Systems", "Curtain Systems"),
    new("OST_CurtainWallMullions", "Curtain Wall Mullions"),
    new("OST_CurtainWallPanels", "Curtain Wall Panels"),
    new("OST_Floors", "Floors"),
    new("OST_Furniture", "Furniture"),
    new("OST_FurnitureSystems", "Furniture Systems"),
    new("OST_Roofs", "Roofs"),
    new("OST_StackedWalls", "Stacked Walls"),
    new("OST_Walls", "Walls"),
    // STRUCTURAL
    new("OST_StructuralColumns", "Structural Columns"),
    new("OST_StructuralFoundation", "Structural Foundation"),
    new("OST_StructuralFraming", "Structural Framing"),
    new("OST_StructuralFramingSystem", "Structural Framing System"),
    new("OST_StructuralTruss", "Structural Truss"),
    // MISC
    new("OST_Levels", "Levels"),
    new("OST_Grids", "Grids"),
    new("OST_Rooms", "Rooms"),
    new("OST_Areas", "Areas"),
    // MEP
    new("OST_DuctCurves", "Duct Curves"),
    new("OST_DuctSystem", "Duct System"),
    new("OST_DuctFitting", "Duct Fitting"),
    new("OST_PipeCurves", "Pipe Curves"),
    new("OST_PipeCurvesCenterLine", "Pipe Curves - Center Line"),
    new("OST_PipeSegments", "Pipe Segments"),
    new("OST_PipeFitting", "Pipe Fitting"),
    new("OST_Conduit", "Conduit"),
    new("OST_ConduitFitting", "Conduit Fitting"),
    new("OST_Cable", "Cable"),
    new("OST_CableTray", "Cable Tray"),
    new("OST_CableTrayFitting", "Cable Tray Fitting")
  ];

  /// <summary>
  /// Gets the human-readable label for a category value.
  /// </summary>
  /// <param name="categoryValue">The category enum name (e.g., "OST_Walls")</param>
  /// <returns>Human-readable label (e.g., "Walls") or the original value if not found</returns>
  public static string GetLabel(string categoryValue)
  {
    var category = Categories.FirstOrDefault(c => c.Value == categoryValue);
    return category?.Label ?? categoryValue;
  }
}
