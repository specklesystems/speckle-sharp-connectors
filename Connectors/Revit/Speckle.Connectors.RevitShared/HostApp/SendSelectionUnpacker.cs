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
///   <term>Curtain walls: </term>
///   <description>If parent wall is part of selection, does not select individual elements out. Otherwise, selects individual elements (Panels, Mullions) as atomic objects.</description>
/// </item>
/// </list>
/// </summary>
public class SendSelectionUnpacker
{
  private readonly IRevitConversionContextStack _contextStack;

  public SendSelectionUnpacker(IRevitConversionContextStack contextStack)
  {
    _contextStack = contextStack;
  }

  public IEnumerable<Element> UnpackSelection(IEnumerable<Element> selectionElements)
  {
    // Note: steps kept separate on purpose.
    // Step 1: unpack groups
    var atomicObjects = UnpackGroups(selectionElements);

    // Step 2: pack curtain wall elements, once we know the full extent of our flattened item list.
    // The behaviour we're looking for:
    // If parent wall is part of selection, does not select individual elements out. Otherwise, selects individual elements (Panels, Mullions) as atomic objects.
    return PackCurtainWallElements(atomicObjects);
  }

  // This needs some yield refactoring
  // TODO: this is now a generic "unpack elements"
  private List<Element> UnpackGroups(IEnumerable<Element> elements)
  {
    var unpackedElements = new List<Element>(); // note: could be a hashset/map so we prevent duplicates (?)

    foreach (var element in elements)
    {
      if (element is Group g)
      {
        var groupElements = g.GetMemberIds().Select(_contextStack.Current.Document.GetElement);
        unpackedElements.AddRange(UnpackGroups(groupElements));
      }
      else if (element is FamilyInstance familyInstance)
      {
        var familyElements = familyInstance.GetSubComponentIds().Select(_contextStack.Current.Document.GetElement);
        unpackedElements.AddRange(UnpackGroups(familyElements));
        unpackedElements.Add(familyInstance);
      }
      else
      {
        unpackedElements.Add(element);
      }
    }

    return unpackedElements;
  }

  private List<Element> PackCurtainWallElements(List<Element> elements)
  {
    var ids = elements.Select(el => el.Id).ToArray();
    elements.RemoveAll(element =>
      (element is Mullion m && ids.Contains(m.Host.Id)) || (element is Panel p && ids.Contains(p.Host.Id))
    );
    return elements;
  }
}
