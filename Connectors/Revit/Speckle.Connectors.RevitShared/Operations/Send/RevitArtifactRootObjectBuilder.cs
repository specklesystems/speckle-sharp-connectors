#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Speckle 4.0 send path for Revit: instead of building a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="RevitObject"/>s and serializing it through the v1 pipeline, this drives the SDK
/// <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly —
/// <c>geometries.parquet</c> (SGEO blobs), <c>eav.*.parquet</c> (properties), <c>envelope.*.parquet</c>
/// (the relations + value-node topology graph) — then uploads the bundle via <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Mirrors speckle-oda's <c>RevitModelExtractor</c> emit shape, reusing the connector's existing
/// converters + proxy caches unchanged (geometry → meshes / <see cref="InstanceProxy"/>,
/// properties → nested dicts, materials/levels/instance-definitions → proxies). The novel connector-only
/// piece is <b>linked models</b>: each source document becomes a <c>CONTAINER</c> node (subtype "Model") and
/// every object emits an <c>IN_MODEL</c> relation to its owning model; a federated (&gt;1 document) send
/// prepends the <c>IN_MODEL</c> tier to the default scene view.</para>
/// <para>.NET 8+ only — the SDK producer types are <c>#if NET8_0_OR_GREATER</c>. The whole file compiles to
/// nothing on the net48 Revit targets (2023/2024); it is registered + used only on net8/net10 (2025+).</para>
/// </remarks>
public class RevitArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings,
  ElementUnpacker elementUnpacker,
  LevelUnpacker levelUnpacker,
  IThreadContext threadContext,
  RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
  LinkedModelHandler linkedModelHandler,
  IArtifactPipelineFactory artifactPipelineFactory,
  ILogger<RevitArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<DocumentToConvert>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<DocumentToConvert> objects,
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
    using var session = ArtefactSessionLog.Start("Revit", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Conversion touches the Revit API → must run on the main thread (same as RevitRootObjectBuilder).
    BundleResult built;
    using (session.Phase("Build"))
    {
      built = await threadContext.RunOnMainAsync(
        () => Task.FromResult(BuildBundleSync(objects, session, versionId, outputDir, onOperationProgressed, cancellationToken))
      );
    }

    // Upload (HTTP) off the main thread.
    return await threadContext.RunOnWorkerAsync(async () =>
    {
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

  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private BundleResult BuildBundleSync(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    ArtefactSessionLog session,
    string versionId,
    string outputDir,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var mainDoc = converterSettings.Current.Document;
    if (mainDoc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    var results = new List<SendConversionResult>();
    bool sendWithLinkedModels = converterSettings.Current.SendLinkedModels;

    if (sendWithLinkedModels)
    {
      linkedModelHandler.PrepareLinkedModelNames(documentElementContexts);
    }

    // ── filter documents / elements (mirrors RevitRootObjectBuilder) ──────────────────
    var filteredDocumentsToConvert = new List<DocumentToConvert>();
    foreach (var documentElementContext in documentElementContexts)
    {
      if (documentElementContext.Doc.IsLinked && !sendWithLinkedModels)
      {
        results.Add(
          new(
            Status.WARNING,
            documentElementContext.Doc.PathName,
            typeof(RevitLinkInstance).ToString(),
            null,
            new SpeckleException("Enable linked model support from the settings to send this object")
          )
        );
        continue;
      }

      var elementsInTransform = new List<Element>();
      foreach (var el in documentElementContext.Elements)
      {
        if (el?.Category == null)
        {
          continue;
        }
        elementsInTransform.Add(el);
      }

      if (elementsInTransform.Count > 0)
      {
        filteredDocumentsToConvert.Add(documentElementContext with { Elements = elementsInTransform });
      }
    }

    if (filteredDocumentsToConvert.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your publish filter!");
    }

    // ── unpack groups / families into atomic objects ─────────────────────────────────
    var atomicObjectsByDocument = new List<DocumentToConvert>();
    var atomicObjectCount = 0;
    foreach (var filteredDocumentToConvert in filteredDocumentsToConvert)
    {
      using (converterSettings.Push(s => s with { Document = filteredDocumentToConvert.Doc }))
      {
        var atomicObjects = elementUnpacker
          .UnpackSelectionForConversion(filteredDocumentToConvert.Elements, filteredDocumentToConvert.Doc)
          .ToList();
        atomicObjectsByDocument.Add(filteredDocumentToConvert with { Elements = atomicObjects });
        atomicObjectCount += atomicObjects.Count;
      }
    }

    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId);

    // element.UniqueId -> the object K(s) it was interned as. A linked element placed by N link instances
    // yields N interned objects (disambiguated by transform hash), so the value is a list. Used to resolve
    // ON_LEVEL post-loop (LevelUnpacker keys by the plain UniqueId).
    var objectKsByElementUniqueId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    var modelContainerKeys = new HashSet<string>(StringComparer.Ordinal);
    var countProgress = 0;
    var skippedObjectCount = 0;

    foreach (var documentContext in atomicObjectsByDocument)
    {
      // One CONTAINER node (subtype "Model") per source document — the IN_MODEL backbone. The host
      // (non-linked) document is keyed "host"; linked documents by their per-instance id. Flat under the
      // root for now (nested-link self-nesting via parentContainerK is a future refinement).
      string modelKey;
      string? modelName;
      if (documentContext.Doc.IsLinked)
      {
        modelKey = linkedModelHandler.GetIdFromDocumentToConvert(documentContext);
        linkedModelHandler.LinkedModelDisplayNames.TryGetValue(modelKey, out modelName);
        modelName ??= documentContext.Doc.Title;
      }
      else
      {
        modelKey = "host";
        modelName = mainDoc.Title;
      }

      int modelK = pipeline.AddContainer(modelKey, modelName, null, "Model");
      modelContainerKeys.Add(modelKey);

      using (
        converterSettings.Push(s =>
          s with
          {
            ReferencePointTransform = documentContext.Transform,
            Document = documentContext.Doc,
          }
        )
      )
      {
        foreach (Element revitElement in documentContext.Elements)
        {
          cancellationToken.ThrowIfCancellationRequested();
          string applicationId = revitElement.UniqueId;
          string sourceType = revitElement.GetType().Name;
          var sw = Stopwatch.StartNew();
          try
          {
            if (!SupportedCategoriesUtils.IsSupportedCategory(revitElement.Category))
            {
              var cat = revitElement.Category != null ? revitElement.Category.Name : "No category";
              results.Add(new(Status.WARNING, revitElement.UniqueId, cat, null, new SpeckleException($"Category {cat} is not supported.")));
              session.RecordObject(applicationId, sourceType, Status.WARNING, $"Category {cat} is not supported", sw.ElapsedMilliseconds);
              skippedObjectCount++;
              continue;
            }

            // Transformed (linked) elements get a transform hash appended so instances of the same linked
            // file under different placements stay distinct — same convention as the v1 builder.
            bool hasTransform = documentContext.Transform != null;
            if (hasTransform)
            {
              string transformHash = linkedModelHandler.GetTransformHash(documentContext.Transform.NotNull());
              applicationId = $"{applicationId}_t{transformHash}";
            }

            Base converted = converter.Convert(revitElement);
            converted.applicationId = applicationId;

            EmitObject(pipeline, converted, modelK, revitElement, applicationId, objectKsByElementUniqueId);
            results.Add(new(Status.SUCCESS, applicationId, sourceType, converted));
            session.RecordObject(applicationId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            logger.LogError(ex, "Failed to convert + emit {SourceType}", sourceType);
            results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
            session.RecordObject(applicationId, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
          }

          onOperationProgressed.Report(new("Converting", (double)++countProgress / atomicObjectCount));
        }
      }
    }

    if (skippedObjectCount == atomicObjectCount)
    {
      throw new SpeckleException("No supported objects visible. Update publish filter or check publish settings.");
    }
    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    EmitValueNodes(pipeline, atomicObjectsByDocument, objectKsByElementUniqueId);

    // Default scene view: Level → Category → Family (the familiar Revit explorer). Prepend the Model tier
    // only when the send actually federates more than one document (linked models) — a single-model send
    // has no meaningful Model grouping.
    var sceneKeys = new List<SceneViewKey>();
    if (modelContainerKeys.Count > 1)
    {
      sceneKeys.Add(SceneViewKey.Rel(RelKind.InModel));
    }
    sceneKeys.Add(SceneViewKey.Rel(RelKind.OnLevel));
    sceneKeys.Add(SceneViewKey.Eav("category"));
    sceneKeys.Add(SceneViewKey.Eav("family"));
    pipeline.AddSceneView(new SceneView(0, "Default", true, sceneKeys));

    pipeline.Complete();

    var bundle = Directory
      .EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = results.Count(r => r.Status == Status.SUCCESS);
    // The artefact path has no serialized root object — a synthetic, deterministic root id (same convention
    // as oda's binary path + the server's "synthetic root" expectation).
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("models", modelContainerKeys.Count);

    logger.LogInformation("Built artefact bundle: {fileCount} files, {objectCount} objects", bundle.Count, objectCount);
    return new BundleResult(bundle, rootId, objectCount, results);
  }

  // Emits one atomic object: its eav labels + IN_MODEL edge + per-fragment DISPLAY (→ geometry) /
  // DISPLAY_INSTANCE (→ INSTANCE node). Instance definitions + materials + levels are wired post-loop.
  private void EmitObject(
    ObjectsArtifactPipeline pipeline,
    Base converted,
    int modelK,
    Element revitElement,
    string applicationId,
    Dictionary<string, List<int>> objectKsByElementUniqueId
  )
  {
    int objK = pipeline.InternObject(applicationId);
    pipeline.InModel(objK, modelK, 0);

    if (!objectKsByElementUniqueId.TryGetValue(revitElement.UniqueId, out var ks))
    {
      ks = new List<int>();
      objectKsByElementUniqueId[revitElement.UniqueId] = ks;
    }
    ks.Add(objK);

    if (converted is not DataObject dataObject)
    {
      // Not a DataObject (no properties / displayValue to flatten) — keep the object in the eav set with
      // just its scalar labels, no geometry/topology.
      pipeline.AddProperties(applicationId, s_emptyProperties, RootScalars(converted, null));
      return;
    }

    var revitObject = converted as RevitObject;
    pipeline.AddProperties(applicationId, dataObject.properties, RootScalars(converted, revitObject), TryGetTypeKey(revitElement));

    int ord = 0;
    foreach (var item in dataObject.displayValue)
    {
      switch (item)
      {
        case InstanceProxy instanceProxy:
          int defK = pipeline.AddDefinition(instanceProxy.definitionId, instanceProxy.definitionId);
          int instK = pipeline.AddInstance(
            instanceProxy.applicationId ?? $"{applicationId}:i{ord}",
            defK,
            Flatten(instanceProxy.transform),
            instanceProxy.units
          );
          pipeline.DisplayInstance(objK, instK, ord++);
          break;

        case SOG.Mesh mesh:
          int gK = pipeline.AddGeometry(mesh.applicationId ?? $"{applicationId}:g{ord}", mesh);
          pipeline.Display(objK, gK, ord++);
          break;

        default:
          // Only meshes (+ instances above) are emitted as renderable geometry. Line/Arc/Curve/etc.
          // display values — e.g. door/window swing arcs and other symbolic 2D curves the converter's
          // geometry walk collects — are intentionally skipped: they aren't real model geometry and show
          // up as spurious arcs/lines in the viewer. Matches oda's mesh-only Revit extraction. Revisit if
          // genuine curve elements (model lines, grids) need rendering.
          break;
      }
    }
  }

  // Definition geometries → InstanceDefinition (DEFINES) → materials (HAS_MATERIAL) → levels (ON_LEVEL).
  // Order matters: all referenced meshes must be added BEFORE the edges that resolve them by applicationId.
  private void EmitValueNodes(
    ObjectsArtifactPipeline pipeline,
    List<DocumentToConvert> atomicObjectsByDocument,
    Dictionary<string, List<int>> objectKsByElementUniqueId
  )
  {
    var flatElements = atomicObjectsByDocument.SelectMany(t => t.Elements).ToList();
    var idsAndSubElementIds = elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(flatElements);

    // 1) shared instance-definition meshes (live outside displayValue, in the cache singleton).
    foreach (var baseObj in revitToSpeckleCacheSingleton.GetBaseObjectsForObjects(idsAndSubElementIds))
    {
      if (baseObj is SOG.Mesh && baseObj.applicationId != null)
      {
        pipeline.AddGeometry(baseObj.applicationId, baseObj);
      }
    }

    // 2) instance definitions → DEFINES edges to their member meshes.
    foreach (var defProxy in revitToSpeckleCacheSingleton.GetInstanceDefinitionProxiesForObjects(idsAndSubElementIds))
    {
      int defK = pipeline.AddDefinition(defProxy.applicationId.NotNull(), defProxy.name);
      int o = 0;
      foreach (var memberAppId in defProxy.objects)
      {
        pipeline.Defines(defK, pipeline.InternGeometryId(memberAppId), o++);
      }
    }

    // 3) render materials → HAS_MATERIAL (geometry → material node).
    foreach (var materialProxy in revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds))
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        value.diffuse,
        value.opacity,
        value.metalness,
        value.roughness
      );
      foreach (var meshAppId in materialProxy.objects)
      {
        pipeline.HasMaterial(pipeline.InternGeometryId(meshAppId), matK);
      }
    }

    // 4) levels → ON_LEVEL. LevelUnpacker keys by the plain element UniqueId, so resolve through the
    // interned-K map to cover linked instances (which were interned with a transform-hash suffix).
    foreach (var levelProxy in levelUnpacker.Unpack(flatElements))
    {
      double elevation = levelProxy.value["elevation"] is double d ? d : 0.0;
      int lvlK = pipeline.AddLevel(levelProxy.applicationId.NotNull(), levelProxy.value.name, elevation);
      foreach (var elementUniqueId in levelProxy.objects)
      {
        if (objectKsByElementUniqueId.TryGetValue(elementUniqueId, out var objKs))
        {
          foreach (var objK in objKs)
          {
            pipeline.OnLevel(objK, lvlK);
          }
        }
      }
    }
  }

  private static KeyValuePair<string, object?>[] RootScalars(Base converted, RevitObject? revitObject) =>
    new KeyValuePair<string, object?>[]
    {
      new("speckle_type", converted.speckle_type),
      new("name", (converted as DataObject)?.name),
      new("units", revitObject?.units),
      new("category", revitObject?.category),
      new("family", revitObject?.family),
      new("type", revitObject?.type),
    };

  // Stable per-type identity (the type element's UniqueId) so type/system parameters dedup into type_eav.
  private string? TryGetTypeKey(Element element)
  {
    var typeId = element.GetTypeId();
    if (typeId == ElementId.InvalidElementId)
    {
      return null;
    }
    return element.Document.GetElement(typeId)?.UniqueId;
  }

  // Matrix4x4 (row-major) → 16 doubles, matching SerializerV2 / Transform.ToArray order.
  private static double[] Flatten(Matrix4x4 m) =>
    new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

  private static readonly IReadOnlyDictionary<string, object?> s_emptyProperties = new Dictionary<string, object?>();

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
#endif
