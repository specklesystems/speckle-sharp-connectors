using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines;
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
  IScalingServiceToSpeckle scalingService,
  IReferencePointConverter referencePointConverter,
  ISpeckleApplication speckleApplication,
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
      built = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(
          BuildBundleSync(objects, session, versionId, outputDir, onOperationProgressed, cancellationToken)
        )
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

    ZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId, producer: speckleApplication);

    // element.UniqueId -> the object K(s) it was interned as. A linked element placed by N link instances
    // yields N interned objects (disambiguated by transform hash), so the value is a list. Used to resolve
    // ON_LEVEL post-loop (LevelUnpacker keys by the plain UniqueId).
    var objectKsByElementUniqueId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    var modelContainerKeys = new HashSet<string>(StringComparer.Ordinal);
    var cameraViews = new List<CameraView>();
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

      EmitMainModelReferencePoint(pipeline, documentContext);

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
              results.Add(
                new(
                  Status.WARNING,
                  revitElement.UniqueId,
                  cat,
                  null,
                  new SpeckleException($"Category {cat} is not supported.")
                )
              );
              session.RecordObject(
                applicationId,
                sourceType,
                Status.WARNING,
                $"Category {cat} is not supported",
                sw.ElapsedMilliseconds
              );
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

        // Named 3D views (perspective AND orthographic) of this document → camera_views rows. Collected inside
        // this settings push so linked-model cameras ride the exact ReferencePointTransform + scaling path the
        // document's element geometry does; linked views get the model display name as a prefix.
        CollectDocumentViews(documentContext.Doc, documentContext.Doc.IsLinked ? modelName : null, cameraViews);
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

    // Named camera viewpoints (host + linked 3D views) → envelope.camera_views.parquet.
    foreach (var cameraView in cameraViews)
    {
      pipeline.AddCameraView(cameraView);
    }

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
    session.SetStat("cameras", cameraViews.Count);

    logger.LogInformation("Built artefact bundle: {fileCount} files, {objectCount} objects", bundle.Count, objectCount);
    return new BundleResult(bundle, rootId, objectCount, results);
  }

  // Revit 3D views → envelope camera_views (Base-free; unlike the v1 Camera path, orthographic views carry over
  // too). MUST run inside the caller's converterSettings.Push for the owning document, so origin/forward/up go
  // through the same ReferencePointTransform + main-document scaling as the document's element geometry (this is
  // what places linked-model cameras correctly in host coordinates). View/Ord = the running dense index.
  private void CollectDocumentViews(Document doc, string? linkedModelName, List<CameraView> cameraViews)
  {
    string units = converterSettings.Current.SpeckleUnits;
    using FilteredElementCollector collector = new(doc);
    var views = collector
      .WhereElementIsNotElementType()
      .OfCategory(BuiltInCategory.OST_Views)
      .Cast<View>()
      .Where(x => x.ViewType == ViewType.ThreeD);

    foreach (View view in views)
    {
      if (view is not View3D view3D || view3D.IsTemplate)
      {
        continue;
      }
      try
      {
        bool isOrtho = !view3D.IsPerspective; // throws InvalidOperationException on some template-ish views
        if (view3D.Origin == null)
        {
          continue; // some 3D views carry no camera position
        }

        ViewOrientation3D orientation = view3D.GetSavedOrientation();
        XYZ origin = referencePointConverter.ConvertToExternalCoordinates(view3D.Origin, true);
        XYZ forward = referencePointConverter.ConvertToExternalCoordinates(orientation.ForwardDirection, false);
        XYZ up = referencePointConverter.ConvertToExternalCoordinates(orientation.UpDirection, false);
        if (forward.IsZeroLength() || up.IsZeroLength())
        {
          continue; // degenerate camera — skip rather than fail the send
        }
        forward = forward.Normalize();
        up = up.Normalize();

        int ord = cameraViews.Count;
        cameraViews.Add(
          new CameraView(
            View: ord,
            Name: linkedModelName is null ? view3D.Name : $"{linkedModelName} - {view3D.Name}",
            IsDefault: false,
            Ord: ord,
            PosX: scalingService.ScaleLength(origin.X),
            PosY: scalingService.ScaleLength(origin.Y),
            PosZ: scalingService.ScaleLength(origin.Z),
            ForwardX: forward.X,
            ForwardY: forward.Y,
            ForwardZ: forward.Z,
            UpX: up.X,
            UpY: up.Y,
            UpZ: up.Z,
            Units: units,
            IsOrtho: isOrtho
          )
        );
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // A single unreadable view (locked orientation, odd template state, …) never fails the send.
        logger.LogWarning(ex, "Skipped unreadable 3D view {ViewName}", view.Name);
      }
    }
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
    pipeline.AddProperties(
      applicationId,
      dataObject.properties,
      RootScalars(converted, revitObject),
      TryGetTypeKey(revitElement)
    );

    EmitDisplayValue(pipeline, objK, applicationId, dataObject.displayValue);

    // Recurse into hosted/nested children (RevitObject.elements) — curtain wall → mullions/panels, railing → top
    // rail, stacked wall → members. RemoveKnownChildElementsWhenParentPresent strips these from the atomic list when
    // the parent is present, so their geometry lives ONLY here; intern each as its own object, emit its geometry, and
    // link it to the parent with a SUBELEMENT edge. Materials resolve by mesh applicationId (idempotent
    // InternGeometryId), which GetElementsAndSubelementIdsFromAtomicObjects already includes for sub-elements.
    if (revitObject is not null)
    {
      int childOrd = 0;
      foreach (RevitObject child in revitObject.elements)
      {
        EmitChild(pipeline, child, modelK, objK, childOrd++);
      }
    }
  }

  // ENG-8947: record the MAIN-model reference point in the bundle meta — the SINGLE source for both federation and
  // Revit→Revit round-trip (the receiver rebuilds the translation from the offset). Only the host (non-linked)
  // document's Transform is the pure reference-point transform — a linked doc's is (referencePoint ∘ linkPlacement⁻¹)
  // and must NOT be persisted. Only the translation kinds are recorded: projectBasePoint / surveyPoint (offset in
  // display units — the vector subtracted) + the requested-but-missing internalOriginFallback. Internal origin and
  // Shared Coordinates record nothing: internal origin has nothing to record, and Shared Coordinates is a
  // connector-only kind outside the shared spec vocabulary whose true-north rotation a translation offset can't
  // represent (so it does not round-trip — a deliberate scope call, see ENG-8808 discussion).
  private void EmitMainModelReferencePoint(ObjectsArtifactPipeline pipeline, DocumentToConvert documentContext)
  {
    if (
      documentContext.Doc.IsLinked
      || converterSettings.Current.ReferencePointKind
        is not (ReferencePointType.ProjectBase or ReferencePointType.Survey)
    )
    {
      return;
    }

    if (documentContext.Transform is { } transform)
    {
      var kind =
        converterSettings.Current.ReferencePointKind == ReferencePointType.ProjectBase
          ? "projectBasePoint"
          : "surveyPoint";
      pipeline.SetReferencePoint(kind, FormatReferencePointOffset(transform));
    }
    else
    {
      // requested a base point the model doesn't have → converted at internal origin, recorded (not silent).
      pipeline.SetReferencePoint("internalOriginFallback", null);
    }
  }

  // ENG-8947 reference_point_offset: the translation subtracted from world-space output, in display units.
  private string FormatReferencePointOffset(Transform transform) =>
    string.Format(
      CultureInfo.InvariantCulture,
      "{0},{1},{2}",
      scalingService.ScaleLength(transform.Origin.X),
      scalingService.ScaleLength(transform.Origin.Y),
      scalingService.ScaleLength(transform.Origin.Z)
    );

  // Emits an object's displayValue as renderable geometry: meshes → DISPLAY, instance proxies → INSTANCE +
  // DISPLAY_INSTANCE, curves/points → DISPLAY (via the SGEO encoder, same as Rhino's artefact send).
  //
  // Curves are role-filtered [ENG-8801]: on an element that ALSO has mesh/instance geometry, curve display values are
  // symbolic 2D (door/window swing arcs) that render as spurious arcs (matches ODA's mesh-only extraction), so they
  // stay suppressed. On an element whose ENTIRE display value is curves/points (model lines, grids, curve-based
  // generic annotations) the curves ARE the geometry — dropping them made the element publish invisible, so we emit
  // them.
  private void EmitDisplayValue(
    ObjectsArtifactPipeline pipeline,
    int objK,
    string appId,
    IReadOnlyList<Base> displayValue
  )
  {
    // Does this element carry real 3D geometry (mesh/instance)? If so, any accompanying curves are symbolic 2D.
    bool hasSolidGeometry = false;
    foreach (var item in displayValue)
    {
      if (item is InstanceProxy or SOG.Mesh)
      {
        hasSolidGeometry = true;
        break;
      }
    }

    int ord = 0;
    foreach (var item in displayValue)
    {
      switch (item)
      {
        case InstanceProxy instanceProxy:
          int defK = pipeline.AddDefinition(instanceProxy.definitionId, instanceProxy.definitionId);
          int instK = pipeline.AddInstance(
            instanceProxy.applicationId ?? $"{appId}:i{ord}",
            defK,
            Flatten(instanceProxy.transform),
            instanceProxy.units
          );
          pipeline.DisplayInstance(objK, instK, ord++);
          break;

        case SOG.Mesh mesh:
          int gK = pipeline.AddGeometry(mesh.applicationId ?? $"{appId}:g{ord}", mesh);
          pipeline.Display(objK, gK, ord++);
          break;

        default:
          // Curves / polylines / points. Emit only on curve/point-only elements (see role-filter note above); on
          // mesh/instance-bearing elements these are swing arcs and stay suppressed.
          if (hasSolidGeometry)
          {
            break;
          }
          try
          {
            int curveK = pipeline.AddGeometry(item.applicationId ?? $"{appId}:g{ord}", item);
            pipeline.Display(objK, curveK, ord++);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            // A display fragment the SGEO encoder doesn't support is skipped without failing the whole object —
            // its properties + topology still land (same tolerance as Rhino's artefact send).
            logger.LogWarning(
              ex,
              "Skipped unsupported curve display geometry {Type} on {AppId}",
              item.speckle_type,
              appId
            );
          }
          break;
      }
    }
  }

  // A hosted/nested child RevitObject: its own interned object (geometry + properties), a SUBELEMENT edge to its
  // owner, and recursion into its own children. Children are NOT in the atomic loop (stripped when the parent is
  // present), so they get no separate ON_LEVEL/type-key resolution here — geometry + hierarchy + materials suffice.
  private void EmitChild(ObjectsArtifactPipeline pipeline, RevitObject child, int modelK, int parentObjK, int subOrd)
  {
    string childAppId = child.applicationId ?? Guid.NewGuid().ToString();
    int childK = pipeline.InternObject(childAppId);
    pipeline.InModel(childK, modelK, 0);
    pipeline.Subelement(parentObjK, childK, subOrd);
    pipeline.AddProperties(childAppId, child.properties, RootScalars(child, child));

    EmitDisplayValue(pipeline, childK, childAppId, child.displayValue);

    int grandOrd = 0;
    foreach (RevitObject grandChild in child.elements)
    {
      EmitChild(pipeline, grandChild, modelK, childK, grandOrd++);
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
    foreach (
      var materialProxy in revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds)
    )
    {
      var value = materialProxy.value;
      int matK = pipeline.AddMaterial(
        materialProxy.applicationId.NotNull(),
        value.name,
        value.diffuse,
        value.opacity,
        value.metalness,
        value.roughness,
        value.emissive,
        value["ior"] as double? // dynamic prop (v1 unpacker convention); null when the host has no IOR [ENG-8791]
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

    // 5) host-API topology from the Revit element graph (rooms / hosting / room-adjacency).
    EmitElementTopology(pipeline, flatElements, objectKsByElementUniqueId);
  }

  // Host-API topology, guarded to sent objects via objectKsByElementUniqueId (only interned targets get an edge):
  //   SUBELEMENT   super-component → element: OWNERSHIP (a nested shared family → its super-component).
  //                Complements the RevitObject.elements lift, which covers children folded INTO a parent object
  //                (curtain panels / mullions / stacked-wall members — ownership too, emitted by EmitChild).
  //   HOSTED_ON    element → its host: PLACEMENT (a door/window → its wall, a fixture → its ceiling). A different
  //                relationship from ownership — a door is placed ON a wall, not a component OF it [ENG-9081].
  //   IN_ROOM      element → its containing Room (or MEP Space) object.
  //   CONNECTS_TO  a door/window's FromRoom → ToRoom, scoped by the opening's object K (the room-adjacency graph).
  // Same accessors as ClassPropertiesExtractor; wrapped defensively — Room/ToRoom/FromRoom can throw without an
  // active phase, and topology is best-effort (must never fail the geometry send).
  private static void EmitElementTopology(
    ObjectsArtifactPipeline pipeline,
    List<Element> flatElements,
    Dictionary<string, List<int>> objectKsByElementUniqueId
  )
  {
    foreach (var element in flatElements)
    {
      if (
        element is not FamilyInstance fi
        || !objectKsByElementUniqueId.TryGetValue(element.UniqueId, out var elementKs)
      )
      {
        continue;
      }
      try
      {
        // Ownership wins over hosting, matching rvextract's precedence (owningElemId first, getHostId as the
        // fallback) so the same fixture gets the same relation whether it is published through the connector or
        // extracted from an uploaded file. An owner that exists but was NOT sent suppresses HOSTED_ON rather than
        // falling through to it — the element is owned, and the edge is simply dropped as dangling.
        if (fi.SuperComponent is { } owner)
        {
          if (objectKsByElementUniqueId.TryGetValue(owner.UniqueId, out var ownerKs))
          {
            foreach (var oK in ownerKs)
            {
              foreach (var eK in elementKs)
              {
                pipeline.Subelement(oK, eK, 0);
              }
            }
          }
        }
        else if (fi.Host is { } host && objectKsByElementUniqueId.TryGetValue(host.UniqueId, out var hostKs))
        {
          foreach (var hK in hostKs)
          {
            foreach (var eK in elementKs)
            {
              pipeline.HostedOn(eK, hK);
            }
          }
        }

        Element? room = fi.Room ?? (Element?)fi.Space;
        if (room is not null && objectKsByElementUniqueId.TryGetValue(room.UniqueId, out var roomKs))
        {
          foreach (var eK in elementKs)
          {
            pipeline.InRoom(eK, roomKs[0], 0);
          }
        }

        if (
          fi.FromRoom is { } fromRoom
          && fi.ToRoom is { } toRoom
          && objectKsByElementUniqueId.TryGetValue(fromRoom.UniqueId, out var fromKs)
          && objectKsByElementUniqueId.TryGetValue(toRoom.UniqueId, out var toKs)
        )
        {
          pipeline.ConnectsTo(fromKs[0], toKs[0], elementKs[0]);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // best-effort: Room/ToRoom/FromRoom can throw without an active phase — skip this element's topology.
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
    new[]
    {
      m.M11,
      m.M12,
      m.M13,
      m.M14,
      m.M21,
      m.M22,
      m.M23,
      m.M24,
      m.M31,
      m.M32,
      m.M33,
      m.M34,
      m.M41,
      m.M42,
      m.M43,
      m.M44,
    };

  private static readonly IReadOnlyDictionary<string, object?> s_emptyProperties = new Dictionary<string, object?>();

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
