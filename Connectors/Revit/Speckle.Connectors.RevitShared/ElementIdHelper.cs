using Autodesk.Revit.DB;
using Speckle.Converters.Common;

namespace Speckle.Connectors.RevitShared;

public static class ElementIdHelper
{
  public static ElementId Parse(string idStr)
  {
    if (int.TryParse(idStr, out var result))
      return new ElementId(result);
    throw new SpeckleConversionException($"Cannot parse ElementId: {idStr}");
  }
}
