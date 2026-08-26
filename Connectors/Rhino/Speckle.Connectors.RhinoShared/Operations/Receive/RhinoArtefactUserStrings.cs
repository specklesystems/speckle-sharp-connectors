using System.Globalization;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Instances;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// Writes an artefact object's eav properties onto Rhino object attributes as user strings, so Rhino user text,
/// nested user dictionaries and foreign parameters (Revit/IFC) survive an artefact receive [ENG-9111].
/// </summary>
/// <remarks>
/// The key shape is the legacy one (<c>SpeckleAttributeExtensions.GetAttributes</c>): nested dictionaries flatten to
/// dot-delimited paths, so <c>{ "User Dictionary": { "phase": 2 } }</c> becomes <c>User Dictionary.phase</c>. Values
/// are formatted with the invariant culture — a Rhino user string is plain text, and formatting a double under the
/// operator's locale would make the same bundle bake differently on different machines (and not re-parse on re-send).
/// </remarks>
internal static class RhinoArtefactUserStrings
{
  private const string PROPERTY_PATH_DELIMITER = ".";

  /// <summary>
  /// Bundle plumbing rather than user data: the root scalars every object carries
  /// (<c>RhinoArtifactRootObjectBuilder.RootScalars</c>) and the hatch styling the send side stashes for
  /// <see cref="RhinoHatchStyler"/>. All of it is either already applied to the native object (name, units, hatch
  /// pattern) or reconstructed on the next send, so writing it as user text would only add noise the user then
  /// sees in the Rhino properties panel.
  /// </summary>
  private static readonly HashSet<string> s_suppressedKeys = new(StringComparer.Ordinal)
  {
    "speckle_type",
    "name",
    "units",
    "type",
    // the definition-member join stamps (@speckle.geometry_k / instance_k) — pure receive plumbing, and now that a
    // member's properties DO reach its attributes they would otherwise surface as user text [ENG-9213].
    DefinitionMemberStamps.STAMP_ROOT,
    "internalDefinitionName",
    "hatchPatternName",
    "hatchRotation",
    "hatchScale",
  };

  /// <summary>Legacy noise filter: the trailing segment of a nested key, e.g. <c>area.units</c>, <c>area.name</c>.</summary>
  private static readonly string[] s_suppressedSuffixes =
  {
    PROPERTY_PATH_DELIMITER + "units",
    PROPERTY_PATH_DELIMITER + "name",
    PROPERTY_PATH_DELIMITER + "internalDefinitionName",
  };

  /// <summary>
  /// Writes <paramref name="properties"/> onto <paramref name="atts"/> as user strings. The keys are the bundle's
  /// dotted paths already (a Rhino user dictionary, or the area/volume records the send side writes, arrive as
  /// <c>group.key</c>), so nothing needs flattening. An empty view is a no-op, so an object with no eav row costs nothing.
  /// </summary>
  public static void Apply(ObjectAttributes atts, PropertyView properties)
  {
    foreach (var kvp in properties)
    {
      string key = kvp.Key;
      if (key.Length == 0)
      {
        continue;
      }
      // Root-scalar plumbing (name/units/type/speckle_type …) and the whole @speckle.* stamp subtree stay out of user text.
      bool isRootScalar = key.IndexOf(PROPERTY_PATH_DELIMITER, StringComparison.Ordinal) < 0;
      if (
        (isRootScalar && s_suppressedKeys.Contains(key))
        || key.StartsWith(DefinitionMemberStamps.STAMP_ROOT + PROPERTY_PATH_DELIMITER, StringComparison.Ordinal)
      )
      {
        continue;
      }
      if (IsSuppressedPath(key))
      {
        continue;
      }
      atts.SetUserString(key, Stringify(kvp.Value));
    }
  }

  private static bool IsSuppressedPath(string key)
  {
    foreach (var suffix in s_suppressedSuffixes)
    {
      if (key.EndsWith(suffix, StringComparison.Ordinal))
      {
        return true;
      }
    }
    return false;
  }

  // Culture-independent text for the value types an eav scalar can hold (string, long, double, bool) — see the
  // <remarks> on why this is not ToString() under the ambient culture.
  private static string Stringify(object? value) =>
    value switch
    {
      null => "",
      string s => s,
      IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
      _ => value.ToString() ?? "",
    };
}
