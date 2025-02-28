using Autodesk.Revit.DB;

namespace Speckle.Connectors.RevitShared;

public static class ElementIdHelper
{
  public static ElementId? GetElementIdFromUniqueId(Document doc, string uniqueId)
  {
    Element element = doc.GetElement(uniqueId);
    return element?.Id;
  }

  public static ElementId? GetElementId2(string elementId)
  {
#if REVIT2024_OR_GREATER
    if (long.TryParse(elementId, out long elementIdInt))
    {
      return new ElementId(elementIdInt);
    }
#else
    if (int.TryParse(elementId, out int elementIdInt))
    {
      return new ElementId(elementIdInt);
    }
#endif
    else
    {
      return null;
    }
  }
}
