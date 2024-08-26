using Autodesk.Revit.DB;
using Speckle.Converters.Common;

namespace Speckle.Connectors.RevitShared;

public static class ElementIdHelper
{
  public static ElementId Parse(string idStr)
  {
    if (!int.TryParse(idStr, out var result))
    {
      throw new SpeckleConversionException($"Cannot parse ElementId: {idStr}");
    }

    return new ElementId(result);
  }

  public static ElementId GetElementIdByUniqueId(Document doc, string uniqueId)
  {
    Element element = doc.GetElement(uniqueId);
    if (element == null)
    {
      throw new SpeckleConversionException($"Cannot find element with UniqueId: {uniqueId}");
    }

    return element.Id;
  }
}
