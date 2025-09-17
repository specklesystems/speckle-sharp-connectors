using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Class that unpacks a given set of selection elements into atomic objects.
/// </summary>
public class ElementUnpacker
{
  /// <summary>
  /// Unpacks a random set of revit objects into atomic objects. It currently unpacks groups recurisvely, nested families into atomic family instances.
  /// This method will also "pack" curtain walls if necessary (ie, if mullions or panels are selected without their parent curtain wall, they are sent independently; if the parent curtain wall is selected, they will be removed out as the curtain wall will include all its children).
  /// </summary>
  /// <param name="selectionElements"></param>
  /// <param name="doc"> We use the nullable document (happiness level 5/10) for the sake of linked models - bc we use this function in 2 different places <br/>
  /// 1- RootObjectBuilder with linked model document - otherwise we cannot unpack elements from correct document.<br/>
  /// 2- Evicting the cache while introducing the settings</param>
  /// <returns></returns>
  public IEnumerable<Element> UnpackSelectionForConversion(IEnumerable<Element> selectionElements, Document doc)
  {
    // Note: steps kept separate on purpose.
    // Step 1: unpack groups
    var atomicObjects = UnpackElements(selectionElements, doc);

    // Step 2: pack curtain wall elements, once we know the full extent of our flattened item list.
    // The behaviour we're looking for:
    // If parent wall is part of selection, does not select individual elements out. Otherwise, selects individual elements (Panels, Mullions) as atomic objects.
    // NOTE: this also conditionally "packs" stacked wall elements if their parent is present. See detailed note inside the function.
    return PackCurtainWallElementsAndStackedWalls(atomicObjects, doc);
  }

  /// <summary>
  /// Unpacks input element ids into their subelements, eg groups and nested family instances
  /// </summary>
  /// <param name="objectIds"></param>
  /// <returns></returns>
  /// <remarks>
  /// This is used to invalidate object ids in the send conversion cache when the selected object id is only the parent element id
  /// </remarks>
  public IEnumerable<string> GetUnpackedElementIds(IEnumerable<string> objectIds, Document doc)
  {
    var docElements = doc.GetElements(objectIds);

    return UnpackSelectionForConversion(docElements, doc).Select(o => o.UniqueId).ToList();
  }

  // We use the nullable document (happiness level 5/10) for the sake of linked models - bc we use this function in 2 different places
  // 1- RootObjectBuilder with linked model document - otherwise we cannot unpack elements from correct document.
  // 2- Evicting the cache while introducing the settings
  private List<Element> UnpackElements(IEnumerable<Element> elements, Document doc)
  {
    var unpackedElements = new List<Element>(); // note: could be a hashset/map so we prevent duplicates (?)
    foreach (var element in elements)
    {
      // UNPACK: Groups
      if (element is Group g)
      {
        // When a group is from a linked model, GetMemberIds may behave differently
        // We add null checks to handle cases where elements can't be properly resolved
        // POC: this might screw up generating hosting rel generation here, because nested families in groups get flattened out by GetMemberIds().
        var groupElements = g.GetMemberIds().Select(doc.GetElement).Where(el => el != null);
        unpackedElements.AddRange(UnpackElements(groupElements, doc));
      }
      else if (element is BaseArray baseArray)
      {
        // For arrays, collect both copied and original members with null checks
        // This handles cases where some elements might not resolve in linked contexts
        var arrayElements = baseArray.GetCopiedMemberIds().Select(doc.GetElement).Where(el => el != null);
        var originalElements = baseArray.GetOriginalMemberIds().Select(doc.GetElement).Where(el => el != null);
        unpackedElements.AddRange(UnpackElements(arrayElements, doc));
        unpackedElements.AddRange(UnpackElements(originalElements, doc));
      }
      // UNPACK: Family instances (as they potentially have nested families inside)
      else if (element is FamilyInstance familyInstance)
      {
        var familyElements = familyInstance
          .GetSubComponentIds()
          .Select(doc.GetElement)
          .Where(el => el != null)
          .ToArray();

        if (familyElements.Length != 0)
        {
          unpackedElements.AddRange(UnpackElements(familyElements, doc));
        }

        unpackedElements.Add(familyInstance);
      }
      else if (element is MultistoryStairs multistoryStairs)
      {
        var stairs = multistoryStairs.GetAllStairsIds().Select(doc.GetElement).Where(el => el != null);
        unpackedElements.AddRange(UnpackElements(stairs, doc));
      }
      else
      {
        unpackedElements.Add(element);
      }
    }
    // Why filtering for duplicates? Well, well, well... it's related to the comment above on groups: if a group
    // contains a nested family, GetMemberIds() will return... duplicates of the exploded family components.

    // Add null check before GroupBy to prevent NullReferenceException when processing linked models with groups
    // This ensures we don't try to access .Id on any null elements that might have been added during the unpacking process
    return unpackedElements.Where(el => el != null).GroupBy(el => el.Id).Select(g => g.First()).ToList(); // no disinctBy in here sadly.
  }

  // We use the nullable document (happiness level 5/10) for the sake of linked models - bc we use this function in 2 different places
  // 1- RootObjectBuilder with linked model document - otherwise we cannot unpack elements from correct document.
  // 2- Evicting the cache while introducing the settings
  private List<Element> PackCurtainWallElementsAndStackedWalls(List<Element> elements, Document doc)
  {
    //just used for contains so use ToHashSet
    var ids = elements.Select(el => el.Id).ToHashSet();

    elements.RemoveAll(element =>
      (element is Mullion { Host: not null } m && ids.Contains(m.Host.Id))
      || (
        element is Panel { Host: not null } p
        && ids.Contains(p.Host.Id)
        && doc.GetElement(p.Host.Id) is not CurtainSystem // don't remove panels when host is CurtainSystem [CNX-1884](https://linear.app/speckle/issue/CNX-1884/revit-curtain-system-not-sending-properly)
      )
      || (
        element is FamilyInstance { Host: not null } f
        && doc.GetElement(f.Host.Id) is Wall { CurtainGrid: not null }
        && ids.Contains(f.Host.Id)
      )
      // NOTE: It is required to explicitly skip stacked wall members because, when getting objects from a view,
      // the api will return the wall parent and its stacked children walls separately. This does not happen
      // via selection. Via category ("Walls") we do not get any parent wall, but just the components of the stacked wall separately.
      // If you wonder why revit is driving people to insanity, this is one of those moments.
      // See [CNX-851: Stacked Wall Duplicate Geometry or Materials not applied](https://linear.app/speckle/issue/CNX-851/stacked-wall-duplicate-geometry-or-materials-not-applied)
      || (element is Wall { IsStackedWallMember: true } wall && ids.Contains(wall.StackedWallOwnerId))
    );
    return elements;
  }

  /// <summary>
  /// Given a set of atomic elements, it will return a list of all their ids as well as their subelements. This currently handles <b>curtain walls</b> and <b>stacked walls</b>.
  /// This might not be an exhaustive list of valid objects with "subelements" in revit, and will need revisiting.
  /// </summary>
  /// <param name="elements"></param>
  /// <returns></returns>
  public List<string> GetElementsAndSubelementIdsFromAtomicObjects(List<Element> elements)
  {
    var ids = new HashSet<string>();
    foreach (var element in elements)
    {
      switch (element)
      {
        case Wall wall:
          if (wall.CurtainGrid is { } grid)
          {
            foreach (var mullionId in grid.GetMullionIds())
            {
              ids.Add(mullionId.ToString());
            }
            foreach (var panelId in grid.GetPanelIds())
            {
              ids.Add(panelId.ToString());
            }
          }
          else if (wall.IsStackedWall)
          {
            foreach (var stackedWallId in wall.GetStackedWallMemberIds())
            {
              ids.Add(stackedWallId.ToString());
            }
          }
          break;
        case FootPrintRoof footPrintRoof:
          if (footPrintRoof.CurtainGrids is { } gs)
          {
            foreach (CurtainGrid roofGrid in gs)
            {
              foreach (var mullionId in roofGrid.GetMullionIds())
              {
                ids.Add(mullionId.ToString());
              }
              foreach (var panelId in roofGrid.GetPanelIds())
              {
                ids.Add(panelId.ToString());
              }
            }
          }
          break;
        default:
          break;
      }

      ids.Add(element.Id.ToString());
    }

    return ids.ToList();
  }
}
