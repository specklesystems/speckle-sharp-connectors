using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Speckle.Converters.RevitShared.Extensions;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Class that unpacks a given set of selection elements into atomic objects.
/// </summary>
public class ElementUnpacker
{
  private static readonly List<BuiltInCategory> s_skippedCategories =
  [
    BuiltInCategory.OST_SketchLines,
    BuiltInCategory.OST_MassForm,
    BuiltInCategory.OST_StairsSketchBoundaryLines,
    BuiltInCategory.OST_StairsSketchLandingCenterLines,
    BuiltInCategory.OST_StairsSketchRiserLines,
    BuiltInCategory.OST_RebarSketchLines,
    BuiltInCategory.OST_StairsSketchRunLines,
  ];

  /// <summary>
  /// Unpacks a random set of revit objects into atomic objects. It currently unpacks groups recursively, nested families into atomic family instances.
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

    // Step 2: Deduplicate parent-child elements in selection
    // Removes child elements (mullions, panels, top rails, stacked wall members) when
    // their parent element is also selected, since parents include children in their conversion.
    // Children are only converted independently when their parent is NOT in the selection.
    return RemoveKnownChildElementsWhenParentPresent(atomicObjects, doc);
  }

  /// <summary>
  /// Unpacks input element ids into their sub-elements, eg groups and nested family instances
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
        var memberIds = g.GetMemberIds();

        if (memberIds.Count <= 0)
        {
          continue;
        }

        // using a collector more efficient
        using var collector = new FilteredElementCollector(doc, memberIds);
        collector.WhereElementIsNotElementType(); // exclude "Type" elements (FamilySymbols)
        var filter = new ElementMulticategoryFilter(s_skippedCategories, inverted: true); // exclude "Sketch/Form" categories
        collector.WherePasses(filter);

        // recursively unpack the valid results
        unpackedElements.AddRange(UnpackElements(collector, doc));
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
  private List<Element> RemoveKnownChildElementsWhenParentPresent(List<Element> elements, Document doc)
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
      // Railings: Remove TopRail when parent railing is selected
      // Prevents duplication since railing converter includes TopRail as a child element
      // TODO: Consider adding HandRail support (also inherits from ContinuousRail)
      || (
        element is TopRail topRail
        && doc.GetElement(topRail.HostRailingId) is Railing railing
        && ids.Contains(railing.Id)
      )
    );
    return elements;
  }

  /// <summary>
  /// Returns element IDs and their known child element IDs for cache tracking.
  /// Uses <see cref="ElementExtensions.GetKnownChildrenElements"/> to determine which children to include.
  /// </summary>
  /// <param name="elements">Elements to process</param>
  /// <returns>Flattened list of parent and child element IDs</returns>
  public List<string> GetElementsAndSubelementIdsFromAtomicObjects(List<Element> elements)
  {
    var ids = new HashSet<string>();
    foreach (var element in elements)
    {
      // add the element's own ID
      ids.Add(element.Id.ToString());

      // add all known children IDs using the extension method. trying to consolidate duplication here with converter
      foreach (var childId in element.GetKnownChildrenElements())
      {
        ids.Add(childId.ToString());
      }
    }

    return ids.ToList();
  }
}
