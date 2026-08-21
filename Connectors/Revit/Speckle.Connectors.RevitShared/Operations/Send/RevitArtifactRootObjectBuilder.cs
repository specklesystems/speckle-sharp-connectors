using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Common.Topology;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared;
using Speckle.Converters.RevitShared.Extensions;
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

    // placement (modelKey) -> element.UniqueId -> the object K it was interned as [ENG-9212]. A linked element
    // placed by N link instances yields N interned objects, ONE per placement — so the identity of an occurrence
    // is (placement, UniqueId), never UniqueId alone. Element topology resolves inside a single placement's map
    // (a door's host is a wall in the same placement); only value-node edges (ON_LEVEL) deliberately span
    // placements, and they flatten this post-loop.
    var objectKsByPlacement = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
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

      // This placement's own identity map. Keyed per modelKey rather than freshly allocated so two contexts that
      // DO share a placement key (coincident transforms — see ENG-9263) degrade to merging, exactly as their
      // interned object Ks already merge, instead of silently splitting the topology of one merged model.
      if (!objectKsByPlacement.TryGetValue(modelKey, out var placementIndex))
      {
        placementIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        objectKsByPlacement[modelKey] = placementIndex;
      }

      // Transformed (linked) elements get a transform hash appended so occurrences of the same linked file under
      // different placements stay distinct — same convention as the v1 builder. Constant per placement, so hash
      // once here instead of per element, and apply it to children as well as atomic objects [ENG-9212].
      var placement = new PlacementContext(
        documentContext.Transform is { } placementTransform
          ? $"_t{linkedModelHandler.GetTransformHash(placementTransform)}"
          : string.Empty,
        placementIndex,
        new HashSet<string>(documentContext.Elements.Select(e => e.UniqueId), StringComparer.Ordinal)
      );

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
        // (element, its object K) for every element of THIS document that actually made it into the bundle —
        // the input to the group pass below, which needs the exact K per document placement (a linked element
        // sent under N link instances is interned N times, once per transform).
        var sentElements = new List<(Element Element, int ObjK)>();

        foreach (Element revitElement in documentContext.Elements)
        {
          cancellationToken.ThrowIfCancellationRequested();
          string applicationId = revitElement.UniqueId + placement.Suffix;
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

            Base converted = converter.Convert(revitElement);
            converted.applicationId = applicationId;

            int objK = EmitObject(pipeline, converted, modelK, revitElement, applicationId, placement);
            sentElements.Add((revitElement, objK));
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

        // Model-group topology for this document, keyed by its model container so a linked file placed twice
        // yields one group tier per placement rather than one shared, cross-wired tier.
        EmitGroups(pipeline, modelKey, sentElements);

        // Host-API element topology for this placement. MUST run after the element loop (a door can be converted
        // before the wall it is hosted on) but needs nothing from any OTHER document: every endpoint Revit reports
        // — SuperComponent, Host, Room, FromRoom/ToRoom — lives in the same document, hence the same placement. So
        // resolving against this placement's map alone is both sufficient and the thing that keeps edges from
        // crossing placements [ENG-9212].
        EmitElementTopology(pipeline, sentElements, placement.ObjectKsByUniqueId);

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

    EmitValueNodes(pipeline, atomicObjectsByDocument, objectKsByPlacement);

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
  // Returns the interned object K so the caller can wire per-document topology (groups) to this exact placement.
  private int EmitObject(
    ObjectsArtifactPipeline pipeline,
    Base converted,
    int modelK,
    Element revitElement,
    string applicationId,
    PlacementContext placement
  )
  {
    int objK = pipeline.InternObject(applicationId);
    pipeline.InModel(objK, modelK, 0);

    // Indexer, not Add: ElementUnpacker already dedups by ElementId so one UniqueId maps to one K per placement,
    // but a coincident-transform placement key (ENG-9263) must degrade quietly rather than throw mid-send.
    placement.ObjectKsByUniqueId[revitElement.UniqueId] = objK;

    if (converted is not DataObject dataObject)
    {
      // Not a DataObject (no properties / displayValue to flatten) — keep the object in the eav set with
      // just its scalar labels, no geometry/topology.
      pipeline.AddProperties(applicationId, s_emptyProperties, RootScalars(converted, null));
      return objK;
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
        EmitChild(pipeline, child, modelK, objK, childOrd++, placement);
      }
    }

    return objK;
  }

  // Record the MAIN-model datum independently from the transform used by conversion. Linked-document transforms
  // also contain occurrence placement and must never be persisted as the model's reference point.
  private void EmitMainModelReferencePoint(ObjectsArtifactPipeline pipeline, DocumentToConvert documentContext)
  {
    if (documentContext.Doc.IsLinked)
    {
      return;
    }

    string requestedKind = converterSettings.Current.ReferencePointKind switch
    {
      ReferencePointType.ProjectBase => "projectBasePoint",
      ReferencePointType.Survey => "surveyPoint",
      ReferencePointType.SharedCoordinates => "sharedCoordinates",
      _ => "internalOrigin",
    };
    Transform? referencePointTransform = converterSettings.Current.ModelReferencePointTransform;
    string referencePointKind = requestedKind;
    if (requestedKind != "internalOrigin" && referencePointTransform is null)
    {
      referencePointKind = "internalOriginFallback";
    }

    var placementData = ReferencePointHelper.GetModelPlacementData(documentContext.Doc);

    // Use the exact transform selected for conversion for the selected option. This keeps the published default
    // and converter output in lockstep even if the source API returns a slightly different Transform instance.
    if (referencePointTransform is not null)
    {
      placementData.SetSourceTransform(requestedKind, referencePointTransform);
    }

    double trueNorthAngle = documentContext.Doc.ActiveProjectLocation!.GetProjectPosition(XYZ.Zero).Angle;
    EmitReferencePointModelRows(
      pipeline,
      referencePointKind,
      requestedKind,
      referencePointTransform,
      placementData,
      trueNorthAngle
    );
  }

  // referencePoint.transform retains Revit's selected source datum for round-tripping. Every
  // modelPlacement.options.*.transform is viewer-ready and maps STORED geometry into that option's coordinate
  // space. This remains true for legacy baked sends: the selected source transform first restores internal
  // coordinates, then the option's inverse datum transform places them in the requested space.
  private void EmitReferencePointModelRows(
    ObjectsArtifactPipeline pipeline,
    string referencePointKind,
    string requestedKind,
    Transform? selectedSourceTransform,
    RevitModelPlacementData placementData,
    double trueNorthAngle
  )
  {
    // Requires Speckle.Objects ≥ speckle-sharp-sdk@oguzhan/bundle-vocab-additions (AddModelProperty API).
    pipeline.AddModelProperty("referencePoint.kind", referencePointKind);
    if (selectedSourceTransform is { } t)
    {
      pipeline.AddModelProperty("referencePoint.transform", FormatReferencePointTransform(t));
      pipeline.AddModelProperty("referencePoint.units", converterSettings.Current.SpeckleUnits);
    }

    string effectiveDefault = referencePointKind == "internalOriginFallback" ? "internalOrigin" : requestedKind;
    Transform defaultPlacement = Transform.Identity;
    foreach (string optionKind in new[] { "internalOrigin", "projectBasePoint", "surveyPoint", "sharedCoordinates" })
    {
      Transform? storedToOption = placementData.GetStoredToOptionTransform(
        optionKind,
        converterSettings.Current.ApplyTransform,
        selectedSourceTransform
      );
      if (storedToOption is null)
      {
        continue;
      }
      pipeline.AddModelProperty(
        $"modelPlacement.options.{optionKind}.transform",
        FormatReferencePointTransform(storedToOption)
      );
      if (optionKind == effectiveDefault)
      {
        defaultPlacement = storedToOption;
      }
    }

    pipeline.AddModelProperty("modelPlacement.default", effectiveDefault);
    pipeline.AddModelProperty("modelPlacement.transform", FormatReferencePointTransform(defaultPlacement));
    pipeline.AddModelProperty("modelPlacement.units", converterSettings.Current.SpeckleUnits);
    pipeline.AddModelProperty("modelPlacement.source", referencePointKind);
    pipeline.AddModelProperty("modelPlacement.appliedToGeometry", converterSettings.Current.ApplyTransform);

    EmitReferencePointPosition(pipeline, "projectBasePoint", placementData.ProjectBasePointPosition);
    EmitReferencePointPosition(pipeline, "surveyPoint", placementData.SurveyPointPosition);
    pipeline.AddModelProperty("projectLocation.trueNorthAngle", trueNorthAngle, "rad");
  }

  private void EmitReferencePointPosition(ObjectsArtifactPipeline pipeline, string kind, XYZ? position)
  {
    if (position is null)
    {
      return;
    }

    string prefix = $"referencePoints.{kind}.position";
    pipeline.AddModelProperty(
      $"{prefix}.x",
      scalingService.ScaleLength(position.X),
      converterSettings.Current.SpeckleUnits
    );
    pipeline.AddModelProperty(
      $"{prefix}.y",
      scalingService.ScaleLength(position.Y),
      converterSettings.Current.SpeckleUnits
    );
    pipeline.AddModelProperty(
      $"{prefix}.z",
      scalingService.ScaleLength(position.Z),
      converterSettings.Current.SpeckleUnits
    );
  }

  // ENG-9099 referencePoint.transform: the full rigid transform subtracted from
  // world-space output, as 16 row-major doubles — same layout Flatten/BuildInstanceTransform already use for
  // InstanceProxy transforms (basis columns unscaled/unit vectors, translation column in display units).
  private string FormatReferencePointTransform(Transform transform)
  {
    var scaled = Transform.Identity;
    scaled.BasisX = transform.BasisX;
    scaled.BasisY = transform.BasisY;
    scaled.BasisZ = transform.BasisZ;
    scaled.Origin = new XYZ(
      scalingService.ScaleLength(transform.Origin.X),
      scalingService.ScaleLength(transform.Origin.Y),
      scalingService.ScaleLength(transform.Origin.Z)
    );
    return ReferencePointHelper.TransformToCsv(scaled);
  }

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
  //
  // Identity is the child's Revit UniqueId (stamped by the converter) qualified by the SAME placement suffix its
  // parent got, so two placements of one linked file keep distinct child identities, a re-send reproduces them, and
  // the child can be the endpoint of a hosting/ownership relation [ENG-9212]. The Guid fallback survives only for a
  // converter that emits an unidentified child — such an object is unreferenceable, but still ships its geometry.
  private void EmitChild(
    ObjectsArtifactPipeline pipeline,
    RevitObject child,
    int modelK,
    int parentObjK,
    int subOrd,
    PlacementContext placement
  )
  {
    string? childUniqueId = child.applicationId;
    string childAppId = childUniqueId is null ? Guid.NewGuid().ToString() : childUniqueId + placement.Suffix;
    int childK = pipeline.InternObject(childAppId);
    pipeline.Subelement(parentObjK, childK, subOrd);

    if (childUniqueId is not null)
    {
      // Reachable as a relation endpoint now that the id is the source UniqueId: a fixture hosted on a curtain
      // panel, or owned by a nested shared family, resolves here instead of being dropped as dangling.
      placement.ObjectKsByUniqueId[childUniqueId] = childK;

      // A composite child can ALSO be atomic in this placement — a basic wall used as a curtain panel is both, and
      // RemoveKnownChildElementsWhenParentPresent only strips Mullion / Panel / curtain-hosted FamilyInstance /
      // stacked-wall member / TopRail. Both emissions now intern to the same deterministic id, so re-emitting the
      // payload here would write its properties twice and hang a second DISPLAY edge (pointing at a second copy of
      // the same mesh) off one object. Keep the ownership edge, let the atomic pass own the payload — it carries
      // ON_LEVEL and the type key on top.
      if (placement.AtomicUniqueIds.Contains(childUniqueId))
      {
        return;
      }
    }

    pipeline.InModel(childK, modelK, 0);
    pipeline.AddProperties(childAppId, child.properties, RootScalars(child, child));

    EmitDisplayValue(pipeline, childK, childAppId, child.displayValue);

    int grandOrd = 0;
    foreach (RevitObject grandChild in child.elements)
    {
      EmitChild(pipeline, grandChild, modelK, childK, grandOrd++, placement);
    }
  }

  // Definition geometries → InstanceDefinition (DEFINES) → materials (HAS_MATERIAL) → levels (ON_LEVEL).
  // Order matters: all referenced meshes must be added BEFORE the edges that resolve them by applicationId.
  private void EmitValueNodes(
    ObjectsArtifactPipeline pipeline,
    List<DocumentToConvert> atomicObjectsByDocument,
    Dictionary<string, Dictionary<string, int>> objectKsByPlacement
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

    // 4) levels → ON_LEVEL. A VALUE-node edge, so this is the one place the fan-out across placements is
    // intentional: a linked element placed N times puts all N occurrences on the (shared) level node. LevelUnpacker
    // keys by the plain element UniqueId, so flatten the per-placement index to reach every occurrence's K
    // (linked ones were interned with a transform-hash suffix).
    var objectKsByElementUniqueId = FlattenPlacementIndexes(objectKsByPlacement);
    foreach (var levelProxy in levelUnpacker.Unpack(flatElements))
    {
      double elevation = levelProxy.value["elevation"] is double d ? d : 0.0;
      int lvlK = pipeline.AddLevel(levelProxy.applicationId.NotNull(), levelProxy.value.name, elevation);
      // flatElements repeats a linked element once per link instance, so LevelUnpacker lists its UniqueId once per
      // placement too. Dedup here or every occurrence's edge is written N times (relations.parquet is an append-only
      // log — nothing downstream collapses duplicate rows) [ENG-9212].
      foreach (var elementUniqueId in levelProxy.objects.Distinct(StringComparer.Ordinal))
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

  // Per-placement identity maps → one element.UniqueId -> every occurrence's object K, across all placements. ONLY
  // for value-node edges, where reaching every occurrence of a repeated linked element is the point; element→element
  // topology must never resolve through this (that is what crossed placements before ENG-9212).
  private static Dictionary<string, List<int>> FlattenPlacementIndexes(
    Dictionary<string, Dictionary<string, int>> objectKsByPlacement
  )
  {
    var objectKsByElementUniqueId = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    foreach (var placementIndex in objectKsByPlacement.Values)
    {
      foreach (var entry in placementIndex)
      {
        if (!objectKsByElementUniqueId.TryGetValue(entry.Key, out var ks))
        {
          ks = new List<int>();
          objectKsByElementUniqueId[entry.Key] = ks;
        }
        ks.Add(entry.Value);
      }
    }
    return objectKsByElementUniqueId;
  }

  // Host-API topology, guarded to sent objects via the placement index (only interned targets get an edge):
  //   SUBELEMENT   super-component → element: OWNERSHIP (a nested shared family → its super-component).
  //                Complements the RevitObject.elements lift, which covers children folded INTO a parent object
  //                (curtain panels / mullions / stacked-wall members — ownership too, emitted by EmitChild).
  //   HOSTED_ON    element → its host: PLACEMENT (a door/window → its wall, a fixture → its ceiling). A different
  //                relationship from ownership — a door is placed ON a wall, not a component OF it [ENG-9081].
  //   IN_ROOM      element → its containing Room (or MEP Space) object.
  //   CONNECTS_TO  a door/window's FromRoom → ToRoom, scoped by the opening's object K (the room-adjacency graph).
  // Same accessors as ClassPropertiesExtractor; wrapped defensively — Room/ToRoom/FromRoom can throw without an
  // active phase, and topology is best-effort (must never fail the geometry send).
  //
  // Scoped to ONE placement [ENG-9212]: <paramref name="sentElements"/> are the objects interned for this document
  // context and <paramref name="placementIndex"/> resolves UniqueId → K within that same context. This half is the
  // Revit read; PlacementTopology owns the resolution (precedence, dangling-endpoint drops, placement scoping) so
  // that logic is testable without a live Revit document — which is what ENG-9212 needed and could not have.
  private static void EmitElementTopology(
    ObjectsArtifactPipeline pipeline,
    List<(Element Element, int ObjK)> sentElements,
    Dictionary<string, int> placementIndex
  )
  {
    var placementElements = new List<PlacementElement>(sentElements.Count);
    foreach (var (element, elementK) in sentElements)
    {
      if (element is not FamilyInstance fi)
      {
        continue;
      }

      string? ownerUniqueId = null;
      string? hostUniqueId = null;
      string? roomUniqueId = null;
      string? fromRoomUniqueId = null;
      string? toRoomUniqueId = null;
      try
      {
        ownerUniqueId = fi.SuperComponent?.UniqueId;
        hostUniqueId = fi.Host?.UniqueId;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // best-effort: topology must never fail the geometry send.
      }
      try
      {
        // Read SEPARATELY from ownership/hosting above: Room/Space/FromRoom/ToRoom throw without an active phase,
        // and a phase-less model should still get its SUBELEMENT / HOSTED_ON edges.
        roomUniqueId = (fi.Room ?? (Element?)fi.Space)?.UniqueId;
        fromRoomUniqueId = fi.FromRoom?.UniqueId;
        toRoomUniqueId = fi.ToRoom?.UniqueId;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // best-effort: no active phase → this element ships without room topology.
      }

      placementElements.Add(
        new PlacementElement(
          element.UniqueId,
          elementK,
          ownerUniqueId,
          hostUniqueId,
          roomUniqueId,
          fromRoomUniqueId,
          toRoomUniqueId
        )
      );
    }

    foreach (var edge in PlacementTopology.Resolve(placementElements, placementIndex))
    {
      switch (edge.Kind)
      {
        case PlacementEdgeKind.Subelement:
          pipeline.Subelement(edge.Src, edge.Dst, edge.Ord);
          break;
        case PlacementEdgeKind.HostedOn:
          pipeline.HostedOn(edge.Src, edge.Dst);
          break;
        case PlacementEdgeKind.InRoom:
          pipeline.InRoom(edge.Src, edge.Dst, edge.Ord);
          break;
        default:
          // ConnectsTo — Ord is the opening's K (a scope, not an ordinal).
          pipeline.ConnectsTo(edge.Src, edge.Dst, edge.Ord);
          break;
      }
    }
  }

  // Revit model groups → CONTAINER("Group") nodes + IN_GROUP membership [ENG-9079]. A SEPARATE axis from the
  // scene tree (IN_MODEL / ON_LEVEL / category eav): an element keeps its model AND its group, exactly as in
  // Rhino/AutoCAD. ElementUnpacker explodes every Group into its members before conversion, so the group
  // INSTANCE is never itself an object in the bundle — the membership survives only on each member's
  // Element.GroupId. Because that unpacking recurses, GroupId always names the INNERMOST group, which is
  // precisely the "at most one IN_GROUP edge per element" contract; the nesting is reproduced as a CONTAINER
  // parent chain (def_ref) walked from each group's own GroupId. The container name is the group TYPE name —
  // the same string ClassPropertiesExtractor publishes as the `groupName` property.
  // Guarded per element: group topology is best-effort and must never fail the geometry send.
  private static void EmitGroups(
    ObjectsArtifactPipeline pipeline,
    string modelKey,
    List<(Element Element, int ObjK)> sentElements
  )
  {
    var containerKByGroupUniqueId = new Dictionary<string, int?>(StringComparer.Ordinal);
    var ordByContainerK = new Dictionary<int, int>();

    foreach (var (element, objK) in sentElements)
    {
      try
      {
        // An ungrouped element's GroupId is InvalidElementId, which resolves to null.
        if (element.Document.GetElement(element.GroupId) is not Group group)
        {
          continue;
        }

        // null = attached detail group: annotation, not model-group membership.
        if (ResolveGroupContainer(pipeline, modelKey, group, containerKByGroupUniqueId) is not int groupK)
        {
          continue;
        }

        int ord = ordByContainerK.TryGetValue(groupK, out int next) ? next : 0;
        pipeline.InGroup(objK, groupK, ord);
        ordByContainerK[groupK] = ord + 1;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // best-effort: a group whose type or parent can't be read is skipped; the element still ships.
      }
    }
  }

  // Interns the CONTAINER("Group") for a placed group, minting its ancestor chain FIRST so a nested group hangs
  // off its parent through def_ref. Keyed by model container as well as group, so a linked file placed twice
  // gets one group tier per placement instead of one shared, cross-wired tier. Memoized per document pass:
  // resolving is a Revit API round-trip and every member of a group asks the same question.
  private static int? ResolveGroupContainer(
    ObjectsArtifactPipeline pipeline,
    string modelKey,
    Group group,
    Dictionary<string, int?> cache
  )
  {
    if (cache.TryGetValue(group.UniqueId, out int? cached))
    {
      return cached;
    }

    int? containerK = null;
    if (!IsAttachedDetailGroup(group))
    {
      int? parentK = group.Document.GetElement(group.GroupId) is Group parent
        ? ResolveGroupContainer(pipeline, modelKey, parent, cache)
        : null;
      containerK = pipeline.AddContainer($"{modelKey}:{group.UniqueId}", group.GroupType.Name, parentK, "Group");
    }

    cache[group.UniqueId] = containerK;
    return containerK;
  }

  // Attached detail groups name the model group they annotate in AttachedParentId; standalone detail groups are
  // caught by category. Both are view-specific annotation and stay out of the model-group tier.
  private static bool IsAttachedDetailGroup(Group group) =>
    group.AttachedParentId != ElementId.InvalidElementId
    || (group.Category is { } category && category.GetBuiltInCategory() == BuiltInCategory.OST_IOSDetailGroups);

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

  /// <summary>
  /// Everything an object — or a child emitted recursively beneath it — needs to be identified and indexed within
  /// ONE linked-model placement [ENG-9212]. A linked file placed N times produces N of these over the same source
  /// document, and nothing may leak between them.
  /// </summary>
  /// <param name="Suffix">Appended to every source UniqueId in this placement: <c>_t{transformHash}</c> for a linked
  /// document, empty for the host document.</param>
  /// <param name="ObjectKsByUniqueId">Source UniqueId → interned object K, for THIS placement only. Element→element
  /// relations resolve here, which is what keeps them from crossing placements.</param>
  /// <param name="AtomicUniqueIds">The UniqueIds converted as atomic objects in this placement — a composite child
  /// that also appears here must not re-emit its payload (see <see cref="EmitChild"/>).</param>
  private sealed record PlacementContext(
    string Suffix,
    Dictionary<string, int> ObjectKsByUniqueId,
    HashSet<string> AtomicUniqueIds
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
