namespace Speckle.Connectors.Rhino.HostApp;

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
  /// Curated list of Revit BuiltInCategory enum names for the lite interop mapper.
  /// These are the exact enum names as they appear in the Revit API.
  /// </summary>
  public static readonly List<string> Categories =
  [
    "OST_Ceilings",
    "OST_Columns",
    "OST_CurtainGrids",
    "OST_CurtainGridsCurtaSystem",
    "OST_CurtainGridsRoof",
    "OST_CurtainGridsSystem",
    "OST_CurtainGridsWall",
    "OST_Curtain_Systems",
    "OST_CurtainWallMullions",
    "OST_CurtainWallPanels",
    "OST_Floors",
    "OST_Furniture",
    "OST_FurnitureSystems",
    "OST_Roofs",
    "OST_StackedWalls",
    "OST_Walls",
    // STRUCTURAL
    "OST_StructuralColumns",
    "OST_StructuralFoundation",
    "OST_StructuralFraming",
    "OST_StructuralFramingSystem",
    "OST_StructuralTruss",
    // MISC
    "OST_Levels",
    "OST_Grids",
    "OST_Rooms",
    "OST_Areas",
    // MEP
    "OST_DuctCurves",
    "OST_DuctSystem",
    "OST_DuctFitting",
    "OST_PipeCurves",
    "OST_PipeCurvesCenterLine",
    "OST_PipeSegments",
    "OST_PipeFitting",
    "OST_Conduit",
    "OST_ConduitFitting",
    "OST_Cable",
    "OST_CableTray",
    "OST_CableTrayFitting"
  ];
}
