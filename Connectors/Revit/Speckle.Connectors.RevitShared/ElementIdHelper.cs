using Autodesk.Revit.DB;
using Speckle.Sdk;

namespace Speckle.Connectors.RevitShared;

public static class ElementIdHelper
{
  public static ElementId GetElementIdFromUniqueId(Document doc, string uniqueId)
  {
    Element element = doc.GetElement(uniqueId);
    if (element == null)
    {
      throw new SpeckleException($"Cannot find element with UniqueId: {uniqueId}");
    }

    return element.Id;
  }
}
