using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

/// <param name="LinkInstance">The <see cref="RevitLinkInstance"/> that places <paramref name="Doc"/> in the host
/// document, or null for the host document itself. This is the placement's identity — see
/// <c>LinkedModelHandler.GetPlacementSuffix</c> for why a hash of <paramref name="Transform"/> is not [ENG-9263].</param>
public record DocumentToConvert(
  Transform? Transform,
  Document Doc,
  List<Element> Elements,
  RevitLinkInstance? LinkInstance = null
);
