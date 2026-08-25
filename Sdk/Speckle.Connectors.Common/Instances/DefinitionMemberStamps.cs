using System.Globalization;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Common.Instances;

/// <summary>
/// The eav stamps that join a block/definition MEMBER's object row back to the K it is addressable by from inside
/// its definition: the member's geometry K (<c>DEFINES</c>) or, for a nested-block member, its INSTANCE node K
/// (<c>DEFINES_INSTANCE</c>).
/// <para><b>Why a stamp and not a relation.</b> A member deliberately carries no top-level <c>DISPLAY</c> /
/// <c>DISPLAY_INSTANCE</c> — it renders solely through a placed instance, and a top-level render edge draws it
/// untransformed at the model origin [ENG-8782]. But <c>ArtefactRelations.ObjectByGeometry()</c> is built from
/// <c>DISPLAY</c> edges alone, so nothing can get from a definition's member geometry back to the member's object
/// row — and therefore nothing can reach what that row carries: its LAYER (<c>IN_COLLECTION</c>) and its
/// properties. The stamp restores the geometry → object direction using eav only, so the member keeps the ordinary
/// object-sourced <c>IN_COLLECTION</c> every top-level object already emits — no new relation, no spec change
/// [ENG-9110].</para>
/// <para><b>Cross-connector contract.</b> The keys and the join are the SketchUp connector's, which solved the
/// same problem for tags first [ENG-8851]. A SketchUp member owns exactly one geometry so it writes a bare
/// integer; a Rhino member can own several (a lossless 3dm solid AND its display meshes, which receive chooses
/// between per member), so the geometry stamp is a comma-joined list. <see cref="Read"/> accepts both forms.</para>
/// <para><b>The invariant this relies on.</b> A member's object row has no render edge, so every consumer that
/// walks objects must skip render-less ones or the member bakes twice. Both C# receive paths already do —
/// the Rhino native builder gates on DISPLAY/SOLID/DISPLAY_INSTANCE, and <c>ObjectsArtifactReader</c> drops an
/// object whose geometry build returns null. Keep it that way.</para>
/// </summary>
public static class DefinitionMemberStamps
{
  /// <summary>The eav path prefix both stamps live under. Read back as a nested dictionary: the eav reader splits
  /// a dotted path into levels, so <c>@speckle.geometry_k</c> arrives as <c>["@speckle"]["geometry_k"]</c>.</summary>
  public const string STAMP_ROOT = "@speckle";

  /// <summary>Leaf key of the geometry stamp — the member's geometry K(s), comma-joined.</summary>
  public const string GEOMETRY_KEY = "geometry_k";

  /// <summary>Leaf key of the nested-block member stamp — the member's INSTANCE node K.</summary>
  public const string INSTANCE_KEY = "instance_k";

  private const string GEOMETRY_PATH = STAMP_ROOT + "." + GEOMETRY_KEY;
  private const string INSTANCE_PATH = STAMP_ROOT + "." + INSTANCE_KEY;

  /// <summary>An empty property tree, for stamping an object that has nothing else to add (the stamp rides the
  /// root scalars). Handy at the call site: <c>AddProperties(appId, NoProperties, GeometryStamp(gKs))</c>.</summary>
  public static IReadOnlyDictionary<string, object?> NoProperties { get; } = new Dictionary<string, object?>();

  /// <summary>Root scalars stamping a definition member with the geometry K(s) it owns. Empty when there are
  /// none — a member whose geometry all failed to encode has nothing to join to.</summary>
  public static KeyValuePair<string, object?>[] GeometryStamp(IReadOnlyCollection<int> geometryKs) =>
    geometryKs.Count == 0
      ? []
      : [new(GEOMETRY_PATH, string.Join(",", geometryKs.Select(k => k.ToString(CultureInfo.InvariantCulture))))];

  /// <summary>Root scalars stamping a NESTED-block member with its INSTANCE node K — the member owns no geometry
  /// of its own, so this is the only handle its definition has on it.</summary>
  public static KeyValuePair<string, object?>[] InstanceStamp(int instanceK) =>
    [new(INSTANCE_PATH, instanceK.ToString(CultureInfo.InvariantCulture))];

  /// <summary>
  /// Inverts the stamps of a whole bundle in one pass: geometry K → owning member object K, and INSTANCE node K →
  /// owning member object K. Feed it <c>ArtefactBundle.Properties</c>. Objects with no stamp (every top-level
  /// object, and every member in a pre-ENG-9110 bundle) contribute nothing, so both maps are empty on older
  /// bundles and callers fall back to their previous behaviour.
  /// </summary>
  public static DefinitionMemberIndex Read(IReadOnlyDictionary<int, Dictionary<string, object?>> propertiesByObject)
  {
    var byGeometry = new Dictionary<int, int>();
    var byInstance = new Dictionary<int, int>();
    foreach (var kv in propertiesByObject)
    {
      if (kv.Value.TryGetValue(STAMP_ROOT, out var root) && root is Dictionary<string, object?> stamps)
      {
        foreach (int geometryK in ParseKs(stamps, GEOMETRY_KEY))
        {
          byGeometry[geometryK] = kv.Key; // a member owns several geometry Ks; all of them point back at it
        }
        foreach (int instanceK in ParseKs(stamps, INSTANCE_KEY))
        {
          byInstance[instanceK] = kv.Key;
        }
      }
    }
    return new DefinitionMemberIndex(byGeometry, byInstance);
  }

  // A stamp value arrives either as a comma-joined string or as a bare number, and BOTH occur for bundles this very
  // class wrote: eav infers a type per value, so "7,8" stays a string (no thousands separator is admitted under the
  // invariant culture) while a single-K "7" is coerced to a number and read back as a double. SketchUp's integer
  // stamp lands in the same numeric branch. Unparseable fragments are skipped rather than thrown: a stamp is an
  // optimisation over a lost layer, never a reason to fail a receive.
  private static List<int> ParseKs(Dictionary<string, object?> stamps, string key)
  {
    var result = new List<int>();
    if (!stamps.TryGetValue(key, out var value) || value is null)
    {
      return result;
    }
    if (value is double d)
    {
      result.Add((int)d);
      return result;
    }
    foreach (var part in Convert.ToString(value, CultureInfo.InvariantCulture)?.Split(',') ?? [])
    {
      if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k))
      {
        result.Add(k);
      }
    }
    return result;
  }
}

/// <summary>The inverted <see cref="DefinitionMemberStamps"/> of one bundle: from a K reachable inside a
/// definition to the member OBJECT K that owns it, which is where the member's layer and properties live.</summary>
public sealed record DefinitionMemberIndex(
  IReadOnlyDictionary<int, int> ObjectByGeometry,
  IReadOnlyDictionary<int, int> ObjectByInstance
);

/// <summary>
/// Builds the <see cref="DefinitionMemberIndex"/> for a bundle, preferring the graph-native join over the eav
/// stamps. Shared because Rhino, SketchUp and Revit all need the same member → object direction [ENG-9110].
/// </summary>
public static class DefinitionMemberIndexes
{
  /// <summary>
  /// The graph-native join (<c>DEFINES_MEMBER</c> 25 / <c>PLACES</c> 24) when the bundle carries it, else the
  /// <c>@speckle.*</c> stamps. Both invert the same direction; a bundle predating the vocab has only the stamps.
  /// </summary>
  public static DefinitionMemberIndex Build(
    ArtefactRelations rels,
    IReadOnlyDictionary<int, Dictionary<string, object?>> propertiesByObject
  ) => TryFromRels(rels) ?? DefinitionMemberStamps.Read(propertiesByObject);

  // Member ↔ geometry joins on (definition, member ordinal), which is immune to the content-hash-dedup collision a
  // geometry-K-keyed inversion cannot distinguish.
  private static DefinitionMemberIndex? TryFromRels(ArtefactRelations rels)
  {
    if (rels.MemberObjectsByDefinition.Count == 0)
    {
      return null;
    }
    var byGeometry = new Dictionary<int, int>();
    var byInstance = new Dictionary<int, int>();
    foreach (var kv in rels.MemberObjectsByDefinition)
    {
      // member ordinal → member object K for this definition
      var objByOrd = new Dictionary<int, int>();
      var memberOrds = rels.MemberOrdByDefinition[kv.Key];
      for (int m = 0; m < kv.Value.Count; m++)
      {
        objByOrd[memberOrds[m]] = kv.Value[m];
      }
      if (
        !rels.DefinesByDefinition.TryGetValue(kv.Key, out var geomKs)
        || !rels.DefinesOrdByDefinition.TryGetValue(kv.Key, out var geomOrds)
      )
      {
        continue;
      }
      for (int i = 0; i < geomKs.Count; i++)
      {
        if (objByOrd.TryGetValue(geomOrds[i], out int memberObjK))
        {
          byGeometry[geomKs[i]] = memberObjK;
        }
      }
    }
    foreach (var kv in rels.PlacesByObject)
    {
      byInstance[kv.Value] = kv.Key;
    }
    return new DefinitionMemberIndex(byGeometry, byInstance);
  }
}
