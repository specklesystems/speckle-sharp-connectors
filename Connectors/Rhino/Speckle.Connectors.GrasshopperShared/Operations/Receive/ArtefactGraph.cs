using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>One relation as the bundle itself declares it - id, spec name, and the dense-int namespace at each end.</summary>
internal sealed record RelationType(byte Rel, string Name, string SourceNamespace, string TargetNamespace);

/// <summary>
/// The envelope graph of one received version: the parsed bundle, the relation catalog it ships with, and every edge
/// indexed in both directions. Backs the Explore component.
/// </summary>
/// <remarks>
/// Nothing here knows what any relation means. The bundle carries its own catalog (<c>rel_types</c>), so a relation
/// added to the spec shows up without a code change - which is the whole point of reading it rather than hardcoding
/// a list.
/// </remarks>
internal sealed class ArtefactGraph
{
  public required ArtefactBundle Bundle { get; init; }

  /// <summary>Relations worth walking - see <see cref="ArtefactGraphCache.IsGeometryNamespace"/> for what's dropped.</summary>
  public required IReadOnlyList<RelationType> RelationTypes { get; init; }

  private readonly Dictionary<byte, Dictionary<int, List<int>>> _forward = new();
  private readonly Dictionary<byte, Dictionary<int, List<int>>> _reverse = new();

  /// <summary>Where this object points: edges it is the source of.</summary>
  public IReadOnlyList<int> Targets(byte rel, int k) => Lookup(_forward, rel, k);

  /// <summary>What points at this object: the same edges read backwards. A wall lists its windows, so a window only
  /// finds its wall this way.</summary>
  public IReadOnlyList<int> Sources(byte rel, int k) => Lookup(_reverse, rel, k);

  internal void Index(byte rel, int src, int dst)
  {
    Index(_forward, rel, src, dst);
    Index(_reverse, rel, dst, src);
  }

  private static IReadOnlyList<int> Lookup(Dictionary<byte, Dictionary<int, List<int>>> map, byte rel, int k) =>
    map.TryGetValue(rel, out var byKey) && byKey.TryGetValue(k, out var found) ? found : [];

  private static void Index(Dictionary<byte, Dictionary<int, List<int>>> map, byte rel, int from, int to)
  {
    if (!map.TryGetValue(rel, out var byKey))
    {
      byKey = new Dictionary<int, List<int>>();
      map[rel] = byKey;
    }
    if (!byKey.TryGetValue(from, out var list))
    {
      list = new List<int>();
      byKey[from] = list;
    }
    list.Add(to);
  }
}

/// <summary>
/// Loads and caches <see cref="ArtefactGraph"/>s from the bundles a receive already left on disk.
/// </summary>
/// <remarks>
/// Reads only what is already cached - it never downloads. A version whose temp folder has been cleared reports as
/// missing and the caller asks the user to reload.
/// </remarks>
internal static class ArtefactGraphCache
{
  private const string NODE_NS = "node";
  private const string OBJECT_NS = "object";
  private const string GEOMETRY_NS = "geometry";

  /// <summary>
  /// Relations touching the geometry namespace (DISPLAY, SOLID, HAS_MATERIAL, HAS_COLOR, DEFINES, …) are skipped.
  /// They're already resolved onto the canvas wrappers during receive, so surfacing them again is noise - and this
  /// stays a rule rather than a list, so it keeps holding if the spec adds more.
  /// </summary>
  public static bool IsGeometryNamespace(string? ns) =>
    ns is not null && ns.IndexOf(GEOMETRY_NS, StringComparison.OrdinalIgnoreCase) >= 0;

  public static bool IsNodeNamespace(string? ns) =>
    ns is not null && ns.IndexOf(NODE_NS, StringComparison.OrdinalIgnoreCase) >= 0;

  public static bool IsObjectNamespace(string? ns) =>
    ns is not null && ns.IndexOf(OBJECT_NS, StringComparison.OrdinalIgnoreCase) >= 0;

  // Parsing a bundle is not cheap, and a solve can ask for the same version many times. Bounded because bundles are
  // large and a long session can touch many versions; oldest-inserted is evicted.
  private const int MAX_CACHED = 3;
  private static readonly Dictionary<string, ArtefactGraph> s_cache = new(StringComparer.Ordinal);
  private static readonly List<string> s_order = new();
  private static readonly object s_lock = new();

  /// <summary>Mirrors the receive-side cache location in <c>ArtifactReceiver</c>.</summary>
  private static string BundleDir(string versionId) =>
    Path.Combine(Path.GetTempPath(), "Speckle", "receive", versionId);

  /// <summary>
  /// The graph for <paramref name="versionId"/>, or null when its bundle is not on disk - either the version was
  /// received via the legacy path, or the temp folder has been cleared.
  /// </summary>
  public static ArtefactGraph? TryGet(string versionId, CancellationToken cancellationToken)
  {
    lock (s_lock)
    {
      if (s_cache.TryGetValue(versionId, out var cached))
      {
        return cached;
      }

      var dir = BundleDir(versionId);
      if (!Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*.parquet").Any())
      {
        return null;
      }

      var graph = Load(dir, cancellationToken);
      s_cache[versionId] = graph;
      s_order.Add(versionId);
      if (s_order.Count > MAX_CACHED)
      {
        s_cache.Remove(s_order[0]);
        s_order.RemoveAt(0);
      }
      return graph;
    }
  }

  private static ArtefactGraph Load(string dir, CancellationToken cancellationToken)
  {
    var bundle = RunSync(() => ArtefactBundleReader.ReadAsync(dir, cancellationToken));
    var types = ReadRelationTypes(dir, cancellationToken);
    var graph = new ArtefactGraph { Bundle = bundle, RelationTypes = types };
    IndexRelations(dir, graph, types, cancellationToken);
    return graph;
  }

  /// <summary>
  /// Blocks on an async read from a synchronous caller. The Task.Run matters: callers run on Grasshopper's UI thread,
  /// and blocking there on a continuation that wants the same thread deadlocks. Off the pool there is no captured
  /// context to come back to. It still blocks the UI for the length of the parse - once per version, not per solve.
  /// </summary>
  private static T RunSync<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

  private static ParquetTable? TryReadTable(string dir, string suffix, CancellationToken cancellationToken)
  {
    var path = Directory
      .EnumerateFiles(dir, "*.parquet")
      .FirstOrDefault(p => p.EndsWith(suffix, StringComparison.Ordinal));
    return path is null ? null : RunSync(() => ParquetTableReader.ReadAsync(path, cancellationToken));
  }

  /// <remarks>
  /// Depends on the envelope column names from the speckle-bundle-spec repo, so a spec change there can break this
  /// independently of the SDK. A bundle with no catalog yields no relations rather than an error.
  /// </remarks>
  private static List<RelationType> ReadRelationTypes(string dir, CancellationToken cancellationToken)
  {
    var types = new List<RelationType>();
    var table = TryReadTable(dir, ".envelope.rel_types.parquet", cancellationToken);
    if (table is null || !table.Has("rel") || !table.Has("name"))
    {
      return types;
    }

    var rel = table.Ints("rel");
    var name = table.Strings("name");
    var srcNs = table.Has("src_ns") ? table.Strings("src_ns") : new string?[rel.Length];
    var dstNs = table.Has("dst_ns") ? table.Strings("dst_ns") : new string?[rel.Length];

    for (int i = 0; i < rel.Length; i++)
    {
      if (name[i] is not { Length: > 0 } relName)
      {
        continue;
      }
      if (IsGeometryNamespace(srcNs[i]) || IsGeometryNamespace(dstNs[i]))
      {
        continue;
      }
      types.Add(new RelationType((byte)rel[i], relName, srcNs[i] ?? "", dstNs[i] ?? ""));
    }

    return types;
  }

  private static void IndexRelations(
    string dir,
    ArtefactGraph graph,
    List<RelationType> types,
    CancellationToken cancellationToken
  )
  {
    if (types.Count == 0)
    {
      return;
    }

    var table = TryReadTable(dir, ".envelope.relations.parquet", cancellationToken);
    if (table is null || !table.Has("rel") || !table.Has("src") || !table.Has("dst"))
    {
      return;
    }

    var wanted = new HashSet<byte>(types.Select(t => t.Rel));
    var rel = table.Ints("rel");
    var src = table.Ints("src");
    var dst = table.Ints("dst");

    for (int i = 0; i < rel.Length; i++)
    {
      var kind = (byte)rel[i];
      if (wanted.Contains(kind))
      {
        graph.Index(kind, src[i], dst[i]);
      }
    }
  }
}
