using System.Globalization;
using Speckle.Connectors.Common.Instances;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Common.Conversion;

/// <summary>
/// The user-facing subset of an artefact object's eav properties: what a receive writes onto the native object
/// (Rhino user strings [ENG-9111], Revit parameters [ENG-10511]). One filter for every host, so a key that is
/// bundle plumbing on one side is plumbing on the other.
/// </summary>
/// <remarks>
/// Values are formatted with the invariant culture: the target is plain text on every host, and formatting a double
/// under the operator's locale would make the same bundle bake differently on different machines.
/// </remarks>
public static class ArtefactUserProperties
{
  public const string PATH_DELIMITER = ".";

  /// <summary>The bundle root every object property lives under (<c>properties.a</c> is user property <c>a</c>).
  /// Storage layout, not user data, so it never reaches the native key.</summary>
  private const string PROPERTIES_ROOT_PREFIX = "properties" + PATH_DELIMITER;

  // Root scalars every object carries (the sender's RootScalars), the definition-member join stamps [ENG-9213] and
  // the hatch styling Rhino stashes for its own receive: all either applied to the native object already or
  // reconstructed on the next send, so writing them back would only add noise.
  private static readonly HashSet<string> s_suppressedKeys = new(StringComparer.Ordinal)
  {
    "speckle_type",
    "name",
    "units",
    "type",
    DefinitionMemberStamps.STAMP_ROOT,
    "internalDefinitionName",
    "hatchPatternName",
    "hatchRotation",
    "hatchScale",
  };

  // Legacy noise filter: the trailing segment of a nested key, e.g. area.units, area.name.
  private static readonly string[] s_suppressedSuffixes =
  {
    PATH_DELIMITER + "units",
    PATH_DELIMITER + "name",
    PATH_DELIMITER + "internalDefinitionName",
  };

  /// <summary>
  /// The user-facing rows of <paramref name="properties"/>, keyed by their dotted path with the <c>properties.</c>
  /// root stripped. An empty view yields nothing, so an object without an eav row costs nothing.
  /// </summary>
  public static IEnumerable<KeyValuePair<string, object?>> Filter(PropertyView properties)
  {
    foreach (var kvp in properties)
    {
      string key = kvp.Key;
      if (key.StartsWith(PROPERTIES_ROOT_PREFIX, StringComparison.Ordinal))
      {
        key = key[PROPERTIES_ROOT_PREFIX.Length..];
      }
      if (key.Length == 0)
      {
        continue;
      }
      bool isRootScalar = key.IndexOf(PATH_DELIMITER, StringComparison.Ordinal) < 0;
      if (
        (isRootScalar && s_suppressedKeys.Contains(key))
        || key.StartsWith(DefinitionMemberStamps.STAMP_ROOT + PATH_DELIMITER, StringComparison.Ordinal)
        || IsSuppressedPath(key)
      )
      {
        continue;
      }
      yield return new KeyValuePair<string, object?>(key, kvp.Value);
    }
  }

  /// <summary>Culture-independent text for the value types an eav scalar can hold (string, double, bool).</summary>
  public static string Stringify(object? value) =>
    value switch
    {
      null => "",
      string s => s,
      IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
      _ => value.ToString() ?? "",
    };

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
}
