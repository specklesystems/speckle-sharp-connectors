using Autodesk.Revit.DB;
using Speckle.Connectors.Revit.Operations.Send.Settings;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Collects rooms and/or areas from a Revit document. Mechanics only — the caller
/// decides whether to append and is responsible for deduplication.
/// </summary>
public class RoomsAndAreasHandler
{
  /// <summary>
  /// Collects rooms and/or areas per <paramref name="mode"/>. Unplaced rooms
  /// (UpperLimit == null) and unplaced areas (Location == null) are excluded.
  /// </summary>
  public IReadOnlyList<Element> CollectRoomsAndAreas(Document document, AppendRoomsAndAreasMode mode)
  {
    if (mode == AppendRoomsAndAreasMode.None)
    {
      return [];
    }

    var collected = new List<Element>();

    if (mode is AppendRoomsAndAreasMode.RoomsOnly or AppendRoomsAndAreasMode.Both)
    {
      using var roomCollector = new FilteredElementCollector(document);
      collected.AddRange(
        roomCollector
          .OfClass(typeof(SpatialElement))
          .OfCategory(BuiltInCategory.OST_Rooms)
          .Cast<Autodesk.Revit.DB.Architecture.Room>()
          .Where(r => r.UpperLimit != null)
      );
    }

    if (mode is AppendRoomsAndAreasMode.AreasOnly or AppendRoomsAndAreasMode.Both)
    {
      using var areaCollector = new FilteredElementCollector(document);
      collected.AddRange(
        areaCollector
          .OfClass(typeof(SpatialElement))
          .OfCategory(BuiltInCategory.OST_Areas)
          .Cast<SpatialElement>()
          .Where(se => se.Location != null)
      );
    }

    return collected;
  }
}
