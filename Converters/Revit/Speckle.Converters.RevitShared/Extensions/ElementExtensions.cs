using Autodesk.Revit.DB;

namespace Speckle.Converters.RevitShared.Extensions;

public static class ElementExtensions
{
  // POC: should this be an injected service?
  public static IList<ElementId> GetHostedElementIds(this Element host)
  {
    IList<ElementId> ids;
    if (host is HostObject hostObject)
    {
      ids = hostObject.FindInserts(true, false, false, false);
    }
    else
    {
      var typeFilter = new ElementIsElementTypeFilter(true);
      var categoryFilter = new ElementMulticategoryFilter(
        new List<BuiltInCategory>()
        {
          BuiltInCategory.OST_CLines,
          BuiltInCategory.OST_SketchLines,
          BuiltInCategory.OST_WeakDims
        },
        true
      );
      ids = host.GetDependentElements(new LogicalAndFilter(typeFilter, categoryFilter));
    }

    // dont include host elementId
    ids.Remove(host.Id);

    return ids;
  }

  public static IEnumerable<ElementId> GetKnownChildrenElements(this Element element) =>
    element switch
    {
      Wall wall => GetWallChildren(wall),
      FootPrintRoof roof => GetFootPrintRoofChildren(roof),
      DBA.Railing railing => GetRailingChildren(railing),
      _ => []
    };

  private static IEnumerable<ElementId> GetWallChildren(Wall wall)
  {
    if (wall.CurtainGrid is CurtainGrid grid)
    {
      foreach (var id in grid.GetMullionIds())
      {
        yield return id;
      }

      foreach (var id in grid.GetPanelIds())
      {
        yield return id;
      }
    }
    else if (wall.IsStackedWall)
    {
      foreach (var id in wall.GetStackedWallMemberIds())
      {
        yield return id;
      }
    }
  }

  private static IEnumerable<ElementId> GetFootPrintRoofChildren(FootPrintRoof footPrintRoof)
  {
    if (footPrintRoof.CurtainGrids is { } grids)
    {
      foreach (CurtainGrid grid in grids)
      {
        foreach (var id in grid.GetMullionIds())
        {
          yield return id;
        }

        foreach (var id in grid.GetPanelIds())
        {
          yield return id;
        }
      }
    }
  }

  private static IEnumerable<ElementId> GetRailingChildren(DBA.Railing railing)
  {
    // TODO: Consider adding HandRail support (railing.GetHandRails())
    if (railing.TopRail != ElementId.InvalidElementId)
    {
      yield return railing.TopRail;
    }
  }
}
