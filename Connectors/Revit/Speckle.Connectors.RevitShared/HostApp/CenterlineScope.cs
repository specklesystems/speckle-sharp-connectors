using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Extensions;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Which elements publish a <c>CENTERLINE</c> from their location curve [ENG-9510].
/// </summary>
/// <remarks>
/// <para>A curated list, not "anything with a location curve". Every element that qualifies adds a geometry blob and
/// a relation row to every Revit send, so the set is opened deliberately rather than by default. MEP was the ask;
/// structural framing came with it.</para>
/// <para>Gated on type and <see cref="BuiltInCategory"/>, never on category NAME — those are localised, so a
/// name-based list silently matches nothing on a German or French model.</para>
/// <para>Does NOT gate MEP fittings: those have no location curve and reach
/// <see cref="MepCenterlineExtractor"/> instead, which scopes itself by connector domain.</para>
/// </remarks>
public static class CenterlineScope
{
  /// <summary>Whether <paramref name="element"/>'s location curve should ship as a centerline.</summary>
  public static bool Includes(Element element) =>
    // Ducts, pipes, conduits and cable trays — plus their flex and placeholder variants, all MEPCurve subclasses.
    element is MEPCurve
    // Beams, braces and girders. A category rather than a type, because that is how Revit models framing.
    || element.Category?.GetBuiltInCategory() is BuiltInCategory.OST_StructuralFraming;
}
