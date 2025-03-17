using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

public record DocumentToConvert(
  Transform? Transform,
  Document Doc,
  List<Element> Elements,
  bool IsLinkedDocument = false
);
