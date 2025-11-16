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

  public static IEnumerable<ElementId> GetKnownChildrenElements(this Element element)
  {
    switch (element)
    {
      // could this be Typed? <T> GetChildren() ?
      case Wall wall:
        var wallChildren = GetWallChildren(wall);
        foreach (var child in wallChildren)
        {
          yield return child;
        }

        break;

      case FootPrintRoof footPrintRoof:
        var footPrintRoofChildren = GetFootPrintRoofChildren(footPrintRoof);
        foreach (var child in footPrintRoofChildren)
        {
          yield return child;
        }

        break;

      case DBA.Railing railing:
        var railingChildren = GetRailingTopRail(railing);
        foreach (var child in railingChildren)
        {
          yield return child;
        }

        break;
    }
  }

  private static IEnumerable<ElementId> GetWallChildren(Wall wall)
  {
    List<ElementId> wallChildrenIds = new();
    if (wall.CurtainGrid is CurtainGrid grid)
    {
      wallChildrenIds.AddRange(grid.GetMullionIds());
      wallChildrenIds.AddRange(grid.GetPanelIds());
    }
    else if (wall.IsStackedWall)
    {
      wallChildrenIds.AddRange(wall.GetStackedWallMemberIds());
    }

    return wallChildrenIds;
  }

  // Shockingly, roofs can have curtain grids on them. I guess it makes sense: https://en.wikipedia.org/wiki/Louvre_Pyramid
  private static IEnumerable<ElementId> GetFootPrintRoofChildren(FootPrintRoof footPrintRoof)
  {
    List<ElementId> footPrintRoofChildrenIds = new();
    if (footPrintRoof.CurtainGrids is { } gs)
    {
      foreach (CurtainGrid grid in gs)
      {
        footPrintRoofChildrenIds.AddRange(grid.GetMullionIds());
        footPrintRoofChildrenIds.AddRange(grid.GetPanelIds());
      }
    }

    return footPrintRoofChildrenIds;
  }

  // Railings should also include toprail which need to be retrieved separately
  private static IEnumerable<ElementId> GetRailingTopRail(DBA.Railing railing)
  {
    // TODO: investigate difference between .TopRail prop and GetHandrails method
    if (railing.TopRail != ElementId.InvalidElementId)
    {
      yield return railing.TopRail;
    }
  }
}
