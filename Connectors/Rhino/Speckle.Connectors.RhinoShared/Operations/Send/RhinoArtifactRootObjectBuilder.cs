using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.Logging;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.HostApp.Properties;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using RhinoLayer = Rhino.DocObjects.Layer;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for Rhino: instead of building a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="Speckle.Objects.Data.RhinoObject"/>s and serializing it through the v1 pipeline, this
/// drives the SDK <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly —
/// <c>geometries.parquet</c> (SGEO blobs + raw 3dm solid blobs), <c>eav.*.parquet</c> (properties),
/// <c>envelope.*.parquet</c> (the relations + value-node topology graph) — then uploads the bundle via
/// <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Mirrors <c>RhinoRootObjectBuilder</c>'s convert/unpack flow (instances, materials, properties,
/// display meshing) but emits into the artefact pipeline. Two Rhino-specific shapes:</para>
/// <list type="number">
///   <item><b>Solids.</b> A Brep/Extrusion/SubD is kept losslessly as the raw 3dm blob the converter already
///   produced (<c>RhinoObject.rawEncoding</c>, base64 3dm). It is written to <c>geometries.parquet</c> verbatim
///   with <c>type = "3dm"</c> and linked via the <c>SOLID</c> rel (for receive-as-solids), <b>alongside</b> its
///   display meshes which link via the <c>DISPLAY</c> rel (for the viewer).</item>
///   <item><b>Layers are the default scene view.</b> The Rhino layer tree becomes <c>COLLECTION</c> nodes
///   (nested via <c>ParentLayerId</c>) with an <c>IN_COLLECTION</c> rel per object, and the default scene view
///   projects on <c>IN_COLLECTION</c> — so the viewer explorer reproduces the layer hierarchy.</item>
/// </list>
/// <para><b>Threading.</b> Two phases. Phase 1 (<see cref="CollectOnMain"/>) runs on the Rhino UI thread —
/// RhinoCommon (convert, unpack, layer/attribute reads) is main-thread-affine — and produces a pure-Speckle
/// snapshot (no RhinoCommon refs). Phase 2 (<see cref="WriteBundle"/>) runs on a worker thread and builds the
/// parquet bundle. This split is mandatory: the artefact pipeline does sync-over-async parquet IO
/// (<c>ParquetWriter.CreateAsync(...).GetAwaiter().GetResult()</c>), which DEADLOCKS on the UI thread (the
/// continuation is posted back to the blocked UI dispatcher/scheduler). Running it on a worker — no UI
/// SynchronizationContext or TaskScheduler — lets those continuations resume on the thread pool. Same principle
/// as speckle-oda, which runs the whole extraction on a dedicated pinned thread off the host UI thread.</para>
/// </remarks>
public class RhinoArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<RhinoConversionSettings> converterSettings,
  RhinoInstanceUnpacker instanceUnpacker,
  RhinoMaterialUnpacker materialUnpacker,
  PropertiesExtractor propertiesExtractor,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ILogger<RhinoArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<RhinoObject>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<RhinoObject> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // Bundle base name = the server pre-allocated versionId, so the parquet files carry their final names
    // from byte one (the v2 upload signs/keys per basename). Each version gets its own scratch dir.
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    Directory.CreateDirectory(outputDir);

    // Per-session diagnostics (per-object timing/failures, phase timings, bundle stats) → %TEMP%\Speckle\sessions\.
    using var session = ArtefactSessionLog.Start("Rhino", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Phase 1 — convert + unpack on the Rhino UI thread (RhinoCommon is main-thread-affine) → pure-Speckle snapshot.
    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(
        () => Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    // Phase 2 — build the parquet bundle + upload on a WORKER thread (no UI SynchronizationContext/TaskScheduler,
    // so the pipeline's sync-over-async parquet IO doesn't deadlock — see the class <remarks>).
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

  // ── Phase 1 (UI thread): RhinoCommon → pure-Speckle snapshot ─────────────────────────────────────────
  private CollectedModel CollectOnMain(
    IReadOnlyList<RhinoObject> rhinoObjects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var doc = converterSettings.Current.Document;
    var units = converterSettings.Current.SpeckleUnits;

    // Unpack instances → atomic objects (incl. block-definition members) + instance/definition proxies.
    var (atomicObjects, _, instanceProxies, instanceDefinitionProxies) = instanceUnpacker.UnpackSelection(rhinoObjects);

    var layers = new Dictionary<int, CollectedLayer>();
    var usedLayers = new List<RhinoLayer>();
    var collectedObjects = new List<CollectedObject>(atomicObjects.Count);
    var results = new List<SendConversionResult>(atomicObjects.Count);

    int count = 0;
    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string applicationId = rhinoObject.Id.ToString();
      string sourceType = rhinoObject.ObjectType.ToString();
      var sw = Stopwatch.StartNew();
      try
      {
        int layerIndex = rhinoObject.Attributes.LayerIndex;
        CollectLayerChain(doc, layerIndex, layers, usedLayers);

        CollectedObject collected = CollectObject(rhinoObject, applicationId, sourceType, layerIndex, units, instanceProxies);
        collectedObjects.Add(collected);
        results.Add(new(Status.SUCCESS, applicationId, sourceType, collected.Converted));
        session.RecordObject(applicationId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert {SourceType}", sourceType);
        results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
        session.RecordObject(applicationId, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
      }

      onOperationProgressed.Report(new("Converting", (double)++count / atomicObjects.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    // Render materials are keyed by material name; proxies list OBJECT ids (resolved to geometry K(s) in phase 2).
    var materials = materialUnpacker.UnpackRenderMaterials(atomicObjects, usedLayers);

    return new CollectedModel(units, collectedObjects, layers, materials, instanceDefinitionProxies, results);
  }

  // Converts one Rhino object to its Speckle representation (instance proxy or geometry) + extracts properties.
  // RhinoCommon-bound (converter.Convert, attribute reads) → phase 1 only. The display/encoding split is left to
  // phase 2 (it's pure Speckle, no RhinoCommon).
  private CollectedObject CollectObject(
    RhinoObject rhinoObject,
    string applicationId,
    string sourceType,
    int layerIndex,
    string units,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies
  )
  {
    string name = !string.IsNullOrEmpty(rhinoObject.Attributes.Name) ? rhinoObject.Attributes.Name : sourceType;

    if (rhinoObject is InstanceObject)
    {
      InstanceProxy instanceProxy = instanceProxies[applicationId];
      var instanceProps = propertiesExtractor.GetProperties(rhinoObject);
      return new CollectedObject(applicationId, name, sourceType, layerIndex, instanceProps, instanceProxy);
    }

    Base rawGeometry = converter.Convert(rhinoObject);
    var properties = propertiesExtractor.GetProperties(rhinoObject);
    propertiesExtractor.AddGeometryProperties(properties, rawGeometry, units);
    return new CollectedObject(applicationId, name, sourceType, layerIndex, properties, rawGeometry);
  }

  // Walks a layer + its ancestors (via ParentLayerId) into the snapshot, keyed by layer index. RhinoCommon-bound.
  private static void CollectLayerChain(
    global::Rhino.RhinoDoc doc,
    int layerIndex,
    Dictionary<int, CollectedLayer> layers,
    List<RhinoLayer> usedLayers
  )
  {
    while (layerIndex >= 0 && !layers.ContainsKey(layerIndex))
    {
      RhinoLayer layer = doc.Layers[layerIndex];
      int? parentIndex = null;
      if (layer.ParentLayerId != Guid.Empty)
      {
        var parent = doc.Layers.FindId(layer.ParentLayerId);
        if (parent != null)
        {
          parentIndex = parent.Index;
        }
      }
      layers[layerIndex] = new CollectedLayer(layer.Id.ToString(), layer.Name, parentIndex);
      usedLayers.Add(layer);
      layerIndex = parentIndex ?? -1;
    }
  }

  // ── Phase 2 (worker thread): pure-Speckle snapshot → parquet bundle ──────────────────────────────────
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

    // Pre-create DEFINITION nodes so they carry their proper name (the per-object pass only has the definitionId).
    foreach (var defProxy in model.Definitions)
    {
      pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
    }

    var layerCollectionKByIndex = new Dictionary<int, int>();
    // object id -> its display-mesh geometry K(s): used post-loop for HAS_MATERIAL and DEFINES.
    var geometryKsByObjectId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    // object id -> its INSTANCE node K, for DEFINES_INSTANCE (nested block placements).
    var instanceKByObjectId = new Dictionary<string, int>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddLayerCollection(pipeline, model.Layers, co.LayerIndex, layerCollectionKByIndex);
      EmitObject(pipeline, co, collK, model.Units, geometryKsByObjectId, instanceKByObjectId);
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    EmitValueNodes(pipeline, model, geometryKsByObjectId, instanceKByObjectId);

    // Default scene view: the Rhino layer tree (IN_COLLECTION). The COLLECTION nodes' parent chain carries the
    // nesting, so a single projection key rebuilds the full explorer hierarchy.
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    pipeline.Complete();

    var bundle = Directory
      .EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = model.Results.Count(r => r.Status == Status.SUCCESS);
    // The artefact path has no serialized root object — a synthetic, deterministic root id (same convention as
    // the Revit artefact builder + the server's "synthetic root" expectation).
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("definitions", model.Definitions.Count);
    session.SetStat("materials", model.Materials.Count);
    session.SetStat("layers", model.Layers.Count);

    logger.LogInformation("Built artefact bundle: {fileCount} files, {objectCount} objects", bundle.Count, objectCount);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  // Emits one object: eav labels + IN_COLLECTION, then a block placement (DISPLAY_INSTANCE → INSTANCE node) or
  // geometry — the lossless 3dm SOLID blob (if present) plus the DISPLAY meshes. Pure Speckle (no RhinoCommon).
  private void EmitObject(
    ObjectsArtifactPipeline pipeline,
    CollectedObject co,
    int collK,
    string units,
    Dictionary<string, List<int>> geometryKsByObjectId,
    Dictionary<string, int> instanceKByObjectId
  )
  {
    int objK = pipeline.InternObject(co.ApplicationId);
    pipeline.InCollection(objK, collK, 0);
    pipeline.AddProperties(co.ApplicationId, co.Properties, RootScalars(co.Converted.speckle_type, co.Name, units, co.SourceType));

    // ── block instance: object → INSTANCE node (transform + definition) via DISPLAY_INSTANCE ──────────
    if (co.Converted is InstanceProxy instanceProxy)
    {
      int defK = pipeline.AddDefinition(instanceProxy.definitionId, null);
      int instK = pipeline.AddInstance(co.ApplicationId, defK, Flatten(instanceProxy.transform), instanceProxy.units);
      pipeline.DisplayInstance(objK, instK, 0);
      instanceKByObjectId[co.ApplicationId] = instK;
      return;
    }

    // ── geometry object ───────────────────────────────────────────────────────────────────────────────
    // Split display meshes from the lossless raw encoding (pure Speckle): Brep/Extrusion/SubD carry BOTH; a
    // plain Mesh/Point/Curve is its own display.
    Base rawGeometry = co.Converted;
    List<Base> displayGeometry;
    RawEncoding? rawEncoding = null;
    if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplay && rawGeometry is SOG.IRawEncodedObject rawEncoded)
    {
      displayGeometry = hasDisplay.displayValue.Cast<Base>().ToList();
      rawEncoding = rawEncoded.encodedValue;
    }
    else if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplayMeshes)
    {
      displayGeometry = hasDisplayMeshes.displayValue.Cast<Base>().ToList();
    }
    else
    {
      displayGeometry = [rawGeometry];
    }

    // Authoritative solid: the raw 3dm blob, kept verbatim for receive-as-solids.
    if (rawEncoding is not null && rawEncoding.format == RawEncodingFormats.RHINO_3DM)
    {
      byte[] solidBytes = Convert.FromBase64String(rawEncoding.contents);
      int solidK = pipeline.AddRawGeometry($"{co.ApplicationId}:solid", solidBytes, RawEncodingFormats.RHINO_3DM);
      pipeline.Solid(objK, solidK, 0);
    }

    // Renderable display meshes (and self-display primitives: points/curves the SGEO encoder supports).
    var gKs = new List<int>();
    int ord = 0;
    foreach (Base fragment in displayGeometry)
    {
      try
      {
        string gAppId = fragment.applicationId ?? $"{co.ApplicationId}:g{ord}";
        int gK = pipeline.AddGeometry(gAppId, fragment);
        pipeline.Display(objK, gK, ord++);
        gKs.Add(gK);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // A display fragment the SGEO encoder doesn't support (hatch/text/…) is skipped without failing the
        // whole object — its solid blob + properties still land.
        logger.LogWarning(ex, "Skipped unsupported display geometry {Type} on {AppId}", fragment.speckle_type, co.ApplicationId);
      }
    }

    geometryKsByObjectId[co.ApplicationId] = gKs;
  }

  // Definition members (DEFINES / DEFINES_INSTANCE) → render materials (HAS_MATERIAL). Order matters: all
  // referenced meshes/instances must exist (added in the object loop) before the edges that resolve them.
  private static void EmitValueNodes(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    Dictionary<string, List<int>> geometryKsByObjectId,
    Dictionary<string, int> instanceKByObjectId
  )
  {
    // 1) instance definitions → DEFINES (member meshes) / DEFINES_INSTANCE (nested block placements).
    foreach (var defProxy in model.Definitions)
    {
      int defK = pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
      int o = 0;
      foreach (var memberId in defProxy.objects)
      {
        if (instanceKByObjectId.TryGetValue(memberId, out var instK))
        {
          pipeline.DefinesInstance(defK, instK, o++);
        }
        else if (geometryKsByObjectId.TryGetValue(memberId, out var memberGKs))
        {
          foreach (var gK in memberGKs)
          {
            pipeline.Defines(defK, gK, o++);
          }
        }
      }
    }

    // 2) render materials → HAS_MATERIAL (geometry → material node). Rhino material proxies list OBJECT ids,
    // so resolve each object to its display-mesh geometry K(s).
    foreach (var materialProxy in model.Materials)
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        value.diffuse,
        value.opacity,
        value.metalness,
        value.roughness
      );
      foreach (var objectId in materialProxy.objects)
      {
        if (geometryKsByObjectId.TryGetValue(objectId, out var gKs))
        {
          foreach (var gK in gKs)
          {
            pipeline.HasMaterial(gK, matK);
          }
        }
      }
    }
  }

  // Resolves (and interns once) the COLLECTION node for a layer index, building the ancestor chain from the
  // collected layer tree so the nesting is reproduced as nested COLLECTION nodes. Cached by layer index.
  private static int GetOrAddLayerCollection(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<int, CollectedLayer> layers,
    int layerIndex,
    Dictionary<int, int> cache
  )
  {
    if (cache.TryGetValue(layerIndex, out var existing))
    {
      return existing;
    }

    CollectedLayer layer = layers[layerIndex];
    int? parentK = null;
    if (layer.ParentIndex is int parentIndex && layers.ContainsKey(parentIndex))
    {
      parentK = GetOrAddLayerCollection(pipeline, layers, parentIndex, cache);
    }

    int collK = pipeline.AddCollection(layer.Id, layer.Name, parentK, "Layer");
    cache[layerIndex] = collK;
    return collK;
  }

  private static KeyValuePair<string, object?>[] RootScalars(string speckleType, string name, string units, string sourceType) =>
    new KeyValuePair<string, object?>[]
    {
      new("speckle_type", speckleType),
      new("name", name),
      new("units", units),
      new("type", sourceType),
    };

  // Matrix4x4 (row-major) → 16 doubles, matching SerializerV2 / Transform.ToArray order.
  private static double[] Flatten(Matrix4x4 m) =>
    new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

#if NETFRAMEWORK
  // Parquet.Net Zstd-compresses row groups via IronCompress's native nironcompress.dll, P/Invoked through a bare
  // [DllImport("nironcompress")]. On .NET Framework that resolves against the process (Rhino.exe) directory, NOT
  // the plugin folder, so the co-deployed native isn't found. Pre-load it by full path once (Windows then matches
  // the bare DllImport to the already-loaded module by base name) — no process-global DLL-search-path mutation.
  [System.Runtime.InteropServices.DllImport(
    "kernel32",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode,
    SetLastError = true
  )]
  [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
  private static extern IntPtr LoadLibrary(string lpFileName);

  private static int s_zstdNativePreloaded;

  private void EnsureZstdNativeLoaded()
  {
    if (System.Threading.Interlocked.Exchange(ref s_zstdNativePreloaded, 1) == 1)
    {
      return;
    }
    try
    {
      var dir = Path.GetDirectoryName(typeof(RhinoArtifactRootObjectBuilder).Assembly.Location);
      var native = Path.Combine(dir ?? string.Empty, "nironcompress.dll");
      if (File.Exists(native))
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

  // ── pure-Speckle snapshot passed from the UI thread (phase 1) to the worker thread (phase 2) ──────────
  private sealed record CollectedLayer(string Id, string Name, int? ParentIndex);

  private sealed record CollectedObject(
    string ApplicationId,
    string Name,
    string SourceType,
    int LayerIndex,
    Dictionary<string, object?> Properties,
    Base Converted // InstanceProxy for block instances, otherwise the converted geometry (Brep/Extrusion/SubD/Mesh/…)
  );

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyDictionary<int, CollectedLayer> Layers,
    IReadOnlyList<RenderMaterialProxy> Materials,
    IReadOnlyList<InstanceDefinitionProxy> Definitions,
    IReadOnlyList<SendConversionResult> Results
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
