using Autodesk.Revit.DB;

namespace Speckle.Connectors.RevitShared;

public static class ElementIdHelper
{
  public static ElementId? GetElementIdFromUniqueId(Document doc, string uniqueId)
  {
    Element element = doc.GetElement(uniqueId);
    return element?.Id;
  }

  public static ElementId? GetElementId(string elementId)
  {
    if (int.TryParse(elementId, out int elementIdInt))
    {
      return new ElementId(elementIdInt);
    }
    else
    {
      return null;
    }
  }
}
