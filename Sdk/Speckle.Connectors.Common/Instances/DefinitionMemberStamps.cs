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
/// between per member), so the geometry stamp is a comma-joined list. both forms are accepted.</para>
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
  private static readonly DefinitionMemberIndex s_empty = new(new Dictionary<int, int>(), new Dictionary<int, int>());

  /// <summary>The graph-native join (<c>DEFINES_MEMBER</c> 25 / <c>PLACES</c> 24). Empty when the bundle has no
  /// definition members.</summary>
  public static DefinitionMemberIndex Build(ArtefactRelations rels) => TryFromRels(rels) ?? s_empty;

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
