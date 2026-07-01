using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using DataObject = Speckle.Objects.Data.DataObject; // disambiguate from System.Windows.Forms.DataObject (ETABS uses WinForms)
using Path = System.IO.Path;

namespace Speckle.Connectors.CSiShared.Builders;

/// <summary>
/// Speckle 4.0 send path for CSi/ETABS: instead of assembling a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="DataObject"/>s and serializing it, this converts each element and drives the SDK
/// <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly — <c>geometries.parquet</c>
/// (SGEO Point/Line/Mesh blobs), <c>eav.*.parquet</c> (properties), <c>envelope.*.parquet</c> (the level→category
/// collection topology) — then uploads via <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Registering this builder in <c>AddCsi</c> is the only switch — <c>SendOperation</c> auto-selects
/// <c>SendViaArtifacts</c>; no binding/UX change.</para>
/// <para>CSi elements have no lossless raw encoding — the display geometry (Point/Line/Mesh from
/// <c>DisplayValueExtractor</c>) IS the geometry, and it's SGEO-encodable, so this only emits <c>DISPLAY</c> geometry.
/// Grouping reuses <see cref="CsiSendCollectionManager.GetCollectionSegments"/> (base: by type; ETABS: level→category)
/// as nested CONTAINER nodes + IN_COLLECTION. **Deferred this pass:** section/material <c>GroupProxy</c>s (CSi has no
/// render materials/colors) and analysis results (a gated, deeply-nested <c>root[analysisResults]</c> blob keyed by
/// element→case→station→step — not eav-shaped; needs its own artefact home).</para>
/// <para><b>Threading.</b> Two-phase like Rhino: the CSi COM <c>SapModel</c> API is main-thread-affine, so phase 1
/// (<see cref="CollectOnMain"/>) converts on the host thread → a pure-Speckle snapshot; phase 2
/// (<see cref="WriteBundle"/>) builds the parquet bundle on a worker (the pipeline's sync-over-async IO deadlocks on a
/// UI SynchronizationContext).</para>
/// </remarks>
public class CsiArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<CsiConversionSettings> converterSettings,
  CsiSendCollectionManager collectionManager,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ILogger<CsiArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<ICsiWrapper>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<ICsiWrapper> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    System.IO.Directory.CreateDirectory(outputDir);

    using var session = ArtefactSessionLog.Start("CSi", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Phase 1 — convert on the host thread (CSi COM API is main-thread-affine) → pure-Speckle snapshot.
    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(
        () => Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    // Phase 2 — build + upload on a worker thread (no UI SynchronizationContext → parquet IO can't deadlock).
    return await threadContext.RunOnWorkerAsync(async () =>
    {
      BundleResult built;
      using (session.Phase("Write"))
      {
        built = WriteBundle(collected, session, versionId, outputDir, onOperationProgressed, cancellationToken);
      }

      using var pipeline = artifactPipelineFactory.CreateInstance(
        projectId,
        ingestionId,
        versionId,
        account,
        outputDir,
        cancellationToken
      );

      onOperationProgressed.Report(new("Uploading...", null));
      string finalVersionId;
      using (session.Phase("Upload"))
      {
        finalVersionId = await pipeline
          .UploadFilesAsync(built.Bundle, built.RootId, built.ObjectCount)
          .ConfigureAwait(false);
      }

      return new ArtifactBuildResult(finalVersionId, built.RootId, built.Results);
    });
  }

  // ── Phase 1 (host thread): CSi API → pure-Speckle snapshot ────────────────────────────────────────────
  private CollectedModel CollectOnMain(
    IReadOnlyList<ICsiWrapper> objects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var units = converterSettings.Current.SpeckleUnits;
    var collected = new List<CollectedObject>(objects.Count);
    var results = new List<SendConversionResult>(objects.Count);

    int count = 0;
    foreach (ICsiWrapper wrapper in objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string sourceType = wrapper.ObjectName;
      var sw = Stopwatch.StartNew();
      try
      {
        Base converted = converter.Convert(wrapper);
        var segments = collectionManager.GetCollectionSegments(converted);
        string appId = converted.applicationId ?? Guid.NewGuid().ToString();
        collected.Add(new CollectedObject(appId, sourceType, converted, segments));
        results.Add(new(Status.SUCCESS, appId, sourceType, converted));
        session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (NotImplementedException ex)
      {
        // Expected for some element types (SAP2000 cable/solid, ETABS link/tendon) — a warning, not a failure.
        results.Add(new(Status.WARNING, wrapper.Name, sourceType, null, ex));
        session.RecordObject(wrapper.Name, sourceType, Status.WARNING, ex.Message, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert {SourceType}", sourceType);
        results.Add(new(Status.ERROR, wrapper.Name, sourceType, null, ex));
        session.RecordObject(wrapper.Name, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
      }

      onOperationProgressed.Report(new("Converting", (double)++count / objects.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return new CollectedModel(units, collected, results);
  }

  // ── Phase 2 (worker thread): snapshot → parquet bundle ────────────────────────────────────────────────
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private BundleResult WriteBundle(
    CollectedModel model,
    ArtefactSessionLog session,
    string versionId,
    string outputDir,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
#if NETFRAMEWORK
    EnsureZstdNativeLoaded();
#endif
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId);
    var collectionKByPath = new Dictionary<string, int>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddCollection(pipeline, co.Segments, collectionKByPath);
      int objK = pipeline.InternObject(co.ApplicationId);
      pipeline.InCollection(objK, collK, 0);

      List<Base> display;
      Dictionary<string, object?>? properties;
      if (co.Converted is DataObject dataObject)
      {
        display = dataObject.displayValue;
        properties = dataObject.properties;
      }
      else
      {
        display = new List<Base> { co.Converted };
        properties = null;
      }

      pipeline.AddProperties(
        co.ApplicationId,
        properties ?? s_emptyProps,
        RootScalars(co.Converted.speckle_type, co.Converted["name"]?.ToString(), model.Units, co.SourceType)
      );

      int ord = 0;
      foreach (Base fragment in display)
      {
        try
        {
          string gAppId = fragment.applicationId ?? $"{co.ApplicationId}:g{ord}";
          int gK = pipeline.AddGeometry(gAppId, fragment);
          pipeline.Display(objK, gK, ord++);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          logger.LogWarning(ex, "Skipped unsupported display geometry {Type} on {AppId}", fragment.speckle_type, co.ApplicationId);
        }
      }

      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    // Default scene view: the CSi collection tree (IN_COLLECTION); the CONTAINER parent chain carries the nesting.
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    pipeline.Complete();

    var bundle = System
      .IO.Directory.EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = model.Results.Count(r => r.Status == Status.SUCCESS);
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("collections", collectionKByPath.Count);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  // Resolves (and interns once) the nested CONTAINER chain for the given collection segments (outermost → leaf).
  private static int GetOrAddCollection(ObjectsArtifactPipeline pipeline, IReadOnlyList<string> segments, Dictionary<string, int> cache)
  {
    int? parentK = null;
    var soFar = "";
    foreach (var raw in segments)
    {
      var name = string.IsNullOrWhiteSpace(raw) ? "Unnamed" : raw;
      soFar = soFar.Length == 0 ? name : soFar + "/" + name;
      if (cache.TryGetValue(soFar, out var existing))
      {
        parentK = existing;
        continue;
      }
      int collK = pipeline.AddCollection(soFar, name, parentK, "Collection");
      cache[soFar] = collK;
      parentK = collK;
    }
    // segments is always non-empty (GetCollectionSegments returns at least one), so parentK is set.
    return parentK ?? pipeline.AddCollection("Model", "Model", null, "Collection");
  }

  private static readonly Dictionary<string, object?> s_emptyProps = new();

  private static KeyValuePair<string, object?>[] RootScalars(string speckleType, string? name, string units, string sourceType) =>
    new KeyValuePair<string, object?>[]
    {
      new("speckle_type", speckleType),
      new("name", name),
      new("units", units),
      new("type", sourceType),
    };

#if NETFRAMEWORK
  [System.Runtime.InteropServices.DllImport(
    "kernel32",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode,
    SetLastError = true
  )]
  [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
  private static extern IntPtr LoadLibrary(string lpFileName);

  private static int s_zstdNativePreloaded;

  // Parquet.Net Zstd-compresses via IronCompress's native nironcompress.dll (bare DllImport). On .NET Framework that
  // resolves against the process dir, not the plugin folder, so pre-load it by full path once.
  private void EnsureZstdNativeLoaded()
  {
    if (System.Threading.Interlocked.Exchange(ref s_zstdNativePreloaded, 1) == 1)
    {
      return;
    }
    try
    {
      var dir = Path.GetDirectoryName(typeof(CsiArtifactRootObjectBuilder).Assembly.Location);
      var native = Path.Combine(dir ?? string.Empty, "nironcompress.dll");
      if (System.IO.File.Exists(native))
      {
        if (LoadLibrary(native) == IntPtr.Zero)
        {
          logger.LogWarning("Failed to pre-load native {Native} (parquet Zstd compression may fail)", native);
        }
      }
      else
      {
        logger.LogWarning("Native {Native} not found next to the plugin; parquet Zstd compression may fail", native);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Could not pre-load the IronCompress native for parquet Zstd compression");
    }
  }
#endif

  private sealed record CollectedObject(string ApplicationId, string SourceType, Base Converted, IReadOnlyList<string> Segments);

  private sealed record CollectedModel(string Units, IReadOnlyList<CollectedObject> Objects, IReadOnlyList<SendConversionResult> Results);

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
