using Autodesk.Revit.DB;
using Speckle.Connectors.Revit.Operations.Send.Settings;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Handles collecting rooms and/or areas from a Revit document.
/// This class is responsible for the mechanics of retrieving spatial elements per the
/// requested mode, but not for making decisions about whether rooms/areas should be
/// appended (which is the responsibility of the calling code)!
/// </summary>
public class RoomsAndAreasHandler
{
  /// <summary>
  /// Collects rooms and/or areas from the document per the requested mode.
  /// This method handles the specifics of element collection but doesn't make decisions
  /// about whether rooms/areas should be appended - that's the caller's responsibility.
  /// Callers are also responsible for deduplicating against any already-collected elements.
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
        roomCollector.OfClass(typeof(SpatialElement)).OfCategory(BuiltInCategory.OST_Rooms).Cast<Element>()
      );
    }

    if (mode is AppendRoomsAndAreasMode.AreasOnly or AppendRoomsAndAreasMode.Both)
    {
      using var areaCollector = new FilteredElementCollector(document);
      collected.AddRange(
        areaCollector.OfClass(typeof(SpatialElement)).OfCategory(BuiltInCategory.OST_Areas).Cast<Element>()
      );
    }

    return collected;
  }
}
