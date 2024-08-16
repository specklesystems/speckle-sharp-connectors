using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Class that unpacks a given set of selection elements into atomic objects.
/// POC: it's a fast solution to a lot of complex problems we don't want to answer now, but should in the near future.
/// What this does:
/// <list type="bullet">
/// <item>
///   <term>Groups: </term>
///   <description>explodes them into sub constituent objects, recursively.</description>
/// </item>
/// <item>
///   <term>Nested families: </term>
///   <description>explodes them into sub constituent objects, recursively.</description>
/// </item>
/// <item>
///   <term>Curtain walls: </term>
///   <description>If parent wall is part of selection, does not select individual elements out. Otherwise, selects individual elements (Panels, Mullions) as atomic objects.</description>
/// </item>
/// </list>
/// </summary>
public class ElementUnpacker
{
  private readonly IRevitConversionContextStack _contextStack;

  public ElementUnpacker(IRevitConversionContextStack contextStack)
  {
    _contextStack = contextStack;
  }

  public IEnumerable<Element> UnpackSelectionForConversion(IEnumerable<Element> selectionElements)
  {
    // Note: steps kept separate on purpose.
    // Step 1: unpack groups
    var atomicObjects = UnpackElements(selectionElements);

    // Step 2: pack curtain wall elements, once we know the full extent of our flattened item list.
    // The behaviour we're looking for:
    // If parent wall is part of selection, does not select individual elements out. Otherwise, selects individual elements (Panels, Mullions) as atomic objects.
    return PackCurtainWallElements(atomicObjects);
  }

  private List<Element> UnpackElements(IEnumerable<Element> elements)
  {
    var unpackedElements = new List<Element>(); // note: could be a hashset/map so we prevent duplicates (?)

    foreach (var element in elements)
    {
      // UNPACK: Groups
      if (element is Group g)
      {
        // POC: this might screw up generating hosting rel generation here, because nested families in groups get flattened out by GetMemberIds().
        var groupElements = g.GetMemberIds().Select(_contextStack.Current.Document.GetElement);
        unpackedElements.AddRange(UnpackElements(groupElements));
      }
      // UNPACK: Family instances (as they potentially have nested families inside)
      else if (element is FamilyInstance familyInstance)
      {
        var familyElements = familyInstance
          .GetSubComponentIds()
          .Select(_contextStack.Current.Document.GetElement)
          .ToArray();

        if (familyElements.Length != 0)
        {
          unpackedElements.AddRange(UnpackElements(familyElements));
        }

        unpackedElements.Add(familyInstance);
      }
      else
      {
        unpackedElements.Add(element);
      }
    }
    // Why filtering for duplicates? Well, well, well... it's related to the comment above on groups: if a group
    // contains a nested family, GetMemberIds() will return... duplicates of the exploded family components.
    return unpackedElements.GroupBy(el => el.Id).Select(g => g.First()).ToList(); // no disinctBy in here sadly.
  }

  private List<Element> PackCurtainWallElements(List<Element> elements)
  {
    var ids = elements.Select(el => el.Id).ToArray();
    elements.RemoveAll(element =>
      (element is Mullion m && ids.Contains(m.Host.Id)) || (element is Panel p && ids.Contains(p.Host.Id))
    );
    return elements;
  }

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
        default:
          break;
      }

      ids.Add(element.Id.ToString());
    }

    return ids.ToList();
  }
}
