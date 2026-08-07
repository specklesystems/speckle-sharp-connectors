namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Object-property keys carrying the AutoCAD colour semantics an artefact COLOR node cannot express — it stores a
/// plain ARGB, so an AutoCAD→AutoCAD round trip flattened every ACI colour to truecolor and every explicit ByBlock
/// to a fixed value [ENG-9117]. The send builder writes these alongside the ARGB edge (so the viewer and non-AutoCAD
/// consumers are unaffected); the receive builder prefers them when rebuilding the entity colour.
/// </summary>
internal static class AutocadColorSemanticKeys
{
  /// <summary><c>"aci"</c> (an AutoCAD Color Index colour, index in <see cref="Index"/>) or <c>"block"</c> (the
  /// entity was explicitly set to ByBlock). Absent for a plain truecolor object, whose ARGB is already lossless.</summary>
  public const string SOURCE = "autocadColorSource";

  /// <summary>The original AutoCAD Color Index (0..256), present only when <see cref="SOURCE"/> is <c>"aci"</c>.</summary>
  public const string INDEX = "autocadColorIndex";
}
