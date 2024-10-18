﻿using Autodesk.Revit.DB;
using Speckle.Sdk;

namespace Speckle.Connectors.RevitShared;

public static class ElementIdHelper
{
  public static ElementId? GetElementIdFromUniqueId(Document doc, string uniqueId)
  {
    Element element = doc.GetElement(uniqueId);
    return element?.Id;
  }
}
