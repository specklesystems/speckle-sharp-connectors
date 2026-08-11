using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// The envelope graph of one received version - the parsed bundle plus the object-to-object relations the SDK's
/// reader drops. Backs the Explore component.
/// </summary>
internal sealed class ArtefactGraph
{
  public required ArtefactBundle Bundle { get; init; }

  /// <summary>rel kind -> source object -> target objects, for <see cref="ArtefactGraphCache.ExtraRelations"/>.</summary>
  public required IReadOnlyDictionary<byte, Dictionary<int, List<int>>> ObjectRelations { get; init; }

  public IReadOnlyList<int> Targets(byte rel, int objK) =>
    ObjectRelations.TryGetValue(rel, out var byObject) && byObject.TryGetValue(objK, out var targets)
      ? targets
      : Array.Empty<int>();
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
  /// <summary>
  /// Object-to-object relations that <c>ArtefactBundle</c> parses and discards: its switch has no case for them, and
  /// its object-to-node set stops at 20. Producers write them (Revit folds HOSTED_ON into SUBELEMENT, Civil3D emits
  /// SUBELEMENT and CONNECTS_TO), so we read the relations table ourselves rather than change the SDK for one
  /// connector. Delete this and use the bundle directly if the SDK ever retains them.
  /// </summary>
  internal static readonly byte[] ExtraRelations =
  [
    RelKind.Subelement,
    RelKind.ConnectsTo,
    RelKind.HostedOn,
    RelKind.Bounds,
  ];

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
    // sync-over-async: Explore resolves inside SolveInstance, which is synchronous
    var bundle = ArtefactBundleReader.ReadAsync(dir, cancellationToken).GetAwaiter().GetResult();
    return new ArtefactGraph { Bundle = bundle, ObjectRelations = ReadExtraRelations(dir, cancellationToken) };
  }

  /// <remarks>
  /// Depends on the envelope relations column names (rel/src/dst) from the speckle-bundle-spec repo, so a spec change
  /// there can break this independently of the SDK. Missing columns are treated as "no relations" rather than an
  /// error, so an older bundle degrades to empty outputs.
  /// </remarks>
  private static Dictionary<byte, Dictionary<int, List<int>>> ReadExtraRelations(
    string dir,
    CancellationToken cancellationToken
  )
  {
    var result = new Dictionary<byte, Dictionary<int, List<int>>>();

    var path = Directory
      .EnumerateFiles(dir, "*.parquet")
      .FirstOrDefault(p => p.EndsWith(".envelope.relations.parquet", StringComparison.Ordinal));
    if (path is null)
    {
      return result;
    }

    var table = ParquetTableReader.ReadAsync(path, cancellationToken).GetAwaiter().GetResult();
    if (!table.Has("rel") || !table.Has("src") || !table.Has("dst"))
    {
      return result;
    }

    var rel = table.Ints("rel");
    var src = table.Ints("src");
    var dst = table.Ints("dst");

    for (int i = 0; i < rel.Length; i++)
    {
      var kind = (byte)rel[i];
      if (Array.IndexOf(ExtraRelations, kind) < 0)
      {
        continue;
      }

      if (!result.TryGetValue(kind, out var byObject))
      {
        byObject = new Dictionary<int, List<int>>();
        result[kind] = byObject;
      }
      if (!byObject.TryGetValue(src[i], out var targets))
      {
        targets = new List<int>();
        byObject[src[i]] = targets;
      }
      targets.Add(dst[i]);
    }

    return result;
  }
}
