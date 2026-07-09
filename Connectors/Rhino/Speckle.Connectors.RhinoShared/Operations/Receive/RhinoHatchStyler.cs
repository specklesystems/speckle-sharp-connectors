using Rhino;
using Rhino.DocObjects;
using Speckle.Sdk.Common;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// Restores hatch pattern styling (pattern name → doc pattern, rotation, scale) carried as EAV by the Rhino artefact
/// send onto a <see cref="RG.Hatch"/> rebuilt from a SGEO Region on receive. Kept out of the host-object builder so the
/// builder's class coupling stays bounded (CA1506).
/// </summary>
internal static class RhinoHatchStyler
{
  /// <summary>Applies the styling found under the object's nested <c>properties</c> subtree (the send flattens
  /// co.Properties under the <c>properties.</c> path prefix). A no-op if the object carries no hatch styling.
  /// <paramref name="docUnits"/> is the receiving document's unit system — pattern scale is unit-scaled to match the
  /// geometry (the Region→Hatch Transform scales the boundary but NOT PatternScale, the same gotcha as text height).</summary>
  public static void Apply(RhinoDoc doc, RG.Hatch hatch, Dictionary<string, object?>? objectProperties, string docUnits)
  {
    if (
      objectProperties is null
      || !objectProperties.TryGetValue("properties", out var sub)
      || sub is not Dictionary<string, object?> props
    )
    {
      return;
    }
    if (props.TryGetValue("hatchPatternName", out var pv) && pv is string patternName && patternName.Length > 0)
    {
      int patternIndex = ResolvePatternIndex(doc, patternName);
      if (patternIndex >= 0)
      {
        hatch.PatternIndex = patternIndex;
      }
    }
    // rotation/scale round-trip through EAV's numeric column, so they come back as double — the same type-stability the
    // pattern-name read above relies on (hence a plain pattern match, no numeric coercion).
    if (props.TryGetValue("hatchRotation", out var rv) && rv is double rotation)
    {
      hatch.PatternRotation = rotation; // an angle — unit-independent
    }
    if (props.TryGetValue("hatchScale", out var sv) && sv is double scale && scale > 0)
    {
      // The boundary geometry was scaled to doc units; PatternScale is a display length that Transform leaves alone, so
      // apply the same source→doc factor here or the pattern renders at the wrong density (e.g. m→mm makes it solid).
      var sourceUnits =
        objectProperties.TryGetValue("units", out var u) && u is string us && us.Length > 0 ? us : docUnits;
      hatch.PatternScale = scale * Units.GetConversionFactor(sourceUnits, docUnits);
    }
  }

  // Resolves a hatch pattern name to a doc pattern index. A fresh Rhino document's table does NOT contain the built-in
  // patterns (Hatch1/Plus/… are known as HatchPattern.Defaults but only materialize when used), so FindName misses on a
  // blank doc — in that case add the matching built-in default. Returns -1 for a custom pattern the doc doesn't know
  // (keeps the converter's default; carrying the full definition is a future step).
  private static int ResolvePatternIndex(RhinoDoc doc, string name)
  {
    var existing = doc.HatchPatterns.FindName(name);
    if (existing != null)
    {
      return existing.Index;
    }
    var builtIn = FindDefaultPattern(name);
    return builtIn != null ? doc.HatchPatterns.Add(builtIn) : -1;
  }

  private static HatchPattern? FindDefaultPattern(string name)
  {
    foreach (
      var d in new[]
      {
        HatchPattern.Defaults.Solid,
        HatchPattern.Defaults.Hatch1,
        HatchPattern.Defaults.Hatch2,
        HatchPattern.Defaults.Hatch3,
        HatchPattern.Defaults.Dash,
        HatchPattern.Defaults.Grid,
        HatchPattern.Defaults.Grid60,
        HatchPattern.Defaults.Plus,
        HatchPattern.Defaults.Squares,
      }
    )
    {
      if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return d;
      }
    }
    return null;
  }
}
