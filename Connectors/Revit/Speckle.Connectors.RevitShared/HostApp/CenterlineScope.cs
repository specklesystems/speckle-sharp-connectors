using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Extensions;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Which elements publish a <c>CENTERLINE</c> from their location curve [ENG-9510].
/// </summary>
/// <remarks>
/// <para>A curated list, not "anything with a location curve". Every element that qualifies adds a geometry blob and
/// a relation row to every Revit send, so the set is opened deliberately: MEP runs and the structural frame, where an
/// axis is the element's defining datum. Walls and railings are excluded.</para>
/// <para>Gated on type and <see cref="BuiltInCategory"/>, never on category NAME — those are localised, so a
/// name-based list silently matches nothing on a German or French model.</para>
/// <para>Does NOT gate MEP fittings: those have no location curve and reach
/// <see cref="MepCenterlineExtractor"/> instead, which scopes itself by connector domain. That also covers
/// fabrication parts, which are not <see cref="MEPCurve"/> but do carry flow connectors.</para>
/// </remarks>
public static class CenterlineScope
{
  /// <summary>Whether <paramref name="element"/>'s location curve should ship as a centerline.</summary>
  /// <remarks>
  /// A category listed here that turns out to be point-placed simply emits nothing — a vertical column or an
  /// isolated footing has no location curve — so listing it costs a type check, not a blob.
  /// </remarks>
  public static bool Includes(Element element) =>
    // Ducts, pipes, conduits and cable trays — plus their flex and placeholder variants, all MEPCurve subclasses.
    element is MEPCurve
    || element.Category?.GetBuiltInCategory()
      is BuiltInCategory.OST_StructuralFraming // beams, braces, joists, girders
        or BuiltInCategory.OST_StructuralColumns // slanted columns; vertical ones are point-placed
        or BuiltInCategory.OST_StructuralTruss // truss chords follow the authored curve
        or BuiltInCategory.OST_StructuralFoundation; // continuous/wall footings; isolated ones are point-placed
}
