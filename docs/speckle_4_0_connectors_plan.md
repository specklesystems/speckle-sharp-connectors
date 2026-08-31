# Speckle 4.0 — Connector-by-Connector Migration Plan

> **Purpose:** the live, phased execution plan for migrating each .NET connector onto the Speckle 4.0
> client-side artefact pipeline. Companion to [`4.0-artefact-rewrite.md`](./4.0-artefact-rewrite.md) (the
> contract/architecture source of truth). This doc tracks **what to do, in what order, and where we are**.
>
> Recovered from session `03de9385` (which died on context overflow). Last updated 2026-07-01.

## Context (one paragraph)

Speckle 4.0 moves model serialization **off the server**. Each connector writes three passive Zstd-Parquet
artefact groups directly — `geometries` (SGEO mesh blobs + raw encodings), `eav.*` (object identity + flattened
properties + type dedup), `envelope.*` (a dense-int node/relation graph + `scene_views`) — base-named by the
server-pre-allocated `versionId`, and uploads them via presigned S3 PUTs. Receive is the inverse: download the
bundle and **bake it natively into the host** — **no `Base`/`Collection`/proxy reconstruction**. The wire contract
is externalized in `speckle-bundle-spec` (schema_version 5; COLLECTION folded into `CONTAINER(7) + subtype`).

**Branches (no worktrees — edit directly on branch):**
- Connectors: `big-truck`
- SDK: `dim/server-v2-data-endpoints-sdk`

## Hard rules (the mandate)

1. Every **sender** introduces `IArtifactRootObjectBuilder<THostObject>` — extracts geometry + topology and writes
   parquet directly. No `Collection`/`DataObject`/proxy graph.
2. Every **receiver** introduces `IArtifactHostObjectBuilder` — reads the neutral `ArtefactBundle` and creates
   **native** host objects/layers/materials/instances. No `Base` graph, no v1 traversal/`RootObjectUnpacker`.
3. **No old `Base`-oriented classes** on the 4.0 path (`DataObject`, `Collection`, `RenderMaterialProxy`,
   `InstanceProxy`, `DefaultTraversal`, the v1 bakers). If a `Base` is unavoidable (e.g. report `source`), use a
   **plain registered `Base`** with `applicationId`/`id` set.
4. v1 path stays **only as legacy fallback** for receiving old versions. No new code grows on it.
5. Refactor shared converters/decoders (add `byte[]` overloads, neutral decode paths) rather than reuse
   Base-producing ones.
6. **Receivers are self-contained, mirroring the Rhino reference builder.** Confirmed user intent: _"we wont be
   using old logics definetely right? because i believe they just slow things up."_
7. **Validation is manual** — the user tests in the host app. The plan only needs each target to build and
   produce/consume a correct bundle.

## Scope & phases

| Connector | Send | Receive | Phase |
|---|---|---|---|
| **Rhino** (net48) | ✅ committed | ✅ native, committed | **Phase 0** — reference impl |
| **Revit** (net8/10) | ✅ committed (net8+) | native (net8+), net48 = fallback | **Phase 1** |
| **AutoCAD / Civil3D** | ✅ native (all TFMs) | ✅ native (all TFMs) | **Phase 2** ✅ done — **PAUSE here** |
| **Plant3D** | ✅ native (send-only) | send-only | **Phase 2** ✅ done |
| **CSi (ETABS)** | ✅ native (net48/net8) | n/a | **Phase 3** ✅ done (send only) |
| **Tekla** | ✅ native (net48) | n/a | **Phase 3** ✅ done (send only) |
| **Grasshopper** | ✅ native (net48) | ❌ (bundle→wrapper, separate) | done — send only |
| ArcGIS | — | — | **EXCLUDED** (unmaintained) |
| Bentley, TSD | — | — | **EXCLUDED** (new connectors, not now) |
| Navisworks | — | — | **EXCLUDED** (handled natively by ODA) |

**Deferred by request** (v1 concepts with no artefact-node equivalent — leave for a later, scoped design):
CSi sections, Civil3D property-sets, analysis-results blobs. Emit geometry + standard topology
(layers/levels/materials/instances) only this pass. (Rhino/AutoCAD groups landed since: `IN_GROUP` (17) was
un-retired in the spec — authored groups now emit `CONTAINER("Group")` nodes + `IN_GROUP` membership edges on
send; receive rebuilds them natively — Rhino group table / AutoCAD group dictionary, with a baseLayerName-suffixed
name purged on re-receive.)

## Cross-cutting requirement — per-session diagnostics logging

> User: _"implement logging per session that i will run. i.e. especially for failed objects, elapsed time etc for
> later us to diagnose issues together."_

`ArtefactSessionLog` (in `Speckle.Connectors.Common/Diagnostics/`, Base-free, shared by all connectors):
- **New timestamped file pair per run** under `%TEMP%\Speckle\sessions\` —
  `{yyyyMMdd-HHmmss}-{connector}-{send|receive}-{versionId}`: a `.ndjson` event stream + a `.summary.txt` footer.
  Runs are **never overwritten**.
- **NDJSON:** one record per object `{ ts, phase, appId, type, status (SUCCESS/WARNING/ERROR), error, elapsedMs }`,
  plus run-level `session_start` / `phase` timings / `bundle_stats` / `session_end`.
- **Phase timers** via an `IDisposable` stopwatch scope (send: collect/write/upload; receive:
  download+parse/materials/atomic/instances). Summary footer = counts + failure breakdown + slowest objects.
- Mirror to the connector `ILogger` (Seq) as before.

---

## Phase 0 — Shared foundation ✅ COMPLETE

- `ArtefactSessionLog` diagnostics built and wired into Rhino/Revit **send** + Rhino **receive**.
- `SceneViewResolver` promoted into the SDK (`speckle-sharp-sdk/src/Speckle.Sdk/Pipelines/Receive/Artifacts/
  SceneViewResolver.cs`) — host-agnostic scene-view → nested-layer resolution (`Segments`, `NodeAncestry`,
  `ResolveEav`); Rhino receiver refactored to consume it.
- Receive plumbing committed: `IArtifactHostObjectBuilder`, `ArtifactReceiver`, `ReceiveOperation` branch, Rhino
  native receiver + DI, `RawEncodingToHost.Convert3dm(byte[])`.
- **Commits:** SDK `7ea929a7`, connectors `545d381b3`.

## Phase 1 — Revit native receive ⚠️ BUILT & TESTED, 3 OPEN ITEMS

`RevitHostObjectArtefactBuilder.cs` (~470 lines, `#if NET8_0_OR_GREATER`,
`Connectors/Revit/Speckle.Connectors.RevitShared/Operations/Receive/`) — self-contained, Base-free, mirrors the
Rhino receiver. SGEO mesh → `TessellatedShapeBuilder` → `DirectShape`; materials via `Material.Create` per-face;
category from `builtInCategory`/display-name; instances via `DirectShapeLibrary` per `DISPLAY_INSTANCE`;
lightweight Comments-param marker for clean re-receive (no v1 Group/`RevitMaterialBaker`/`RevitGroupBaker`).
Registered in `RevitConnectorModule.cs` under `#if NET8_0_OR_GREATER`. **Builds clean** (net8 native + net48
fallback). Test run confirmed working: 7133 DirectShapes, 0 errors, marker stamped (so the native path ran, not v1).

**Open items before Phase 1 can close:**

1. **Render materials missing.** Root cause not yet identified. Inspect the received bundle (e.g.
   `%TEMP%\speckle\receive\f0ea106560\`): count MATERIAL nodes (`node_kind=3`) in `envelope.nodes.parquet` and
   HAS_MATERIAL relations in `envelope.relations.parquet`, and check whether HAS_MATERIAL **source geometry keys**
   intersect the DISPLAY relation's **destination geometry keys**. This distinguishes a send-side phantom-geometry
   bug (material-proxy mesh appIds ≠ `mesh.applicationId` → phantom `geometryK`) from a receive-side application
   bug. _Blocked last session:_ pyarrow + DuckDB CLI both missing → `python -m pip install pyarrow` first.
2. **Report shows "Base > Direct Shape".** Cosmetic but the user flagged it (_"are we still dealing with Base etc?
   we should not"_). Comes from the throwaway `Source(appId) => new Base{...}` that `ReceiveConversionResult`
   requires (`SourceType = source.speckle_type` → "Base"). Fix without reintroducing Base data-modeling — likely an
   optional explicit `sourceType` param/overload on `ReceiveConversionResult`, fed the real object type/category
   from the bundle.
3. **No Revit receive session log written** for the test run despite the builder calling `ArtefactSessionLog.Start`
   — investigate (only Rhino receive logs were present).

## Phase 2 — AutoCAD family (send + receive), then PAUSE

Strongest fit after Rhino: present on `big-truck`, full proxy set, Rhino-style raw-encoding solid path
(`Solid3dToRawEncodingConverter` → ACIS **SAT**, `RawEncoding{format=ACAD_SAT}`) maps onto `SOLID`(raw)/`DISPLAY`
(SGEO). TFMs net48 (2023/24) → net8 (2025/26) → net10 (2027) — Rhino's net48 native-zstd preload + STJ handling apply.

**Send — `AutocadArtifactRootObjectBuilder : IArtifactRootObjectBuilder<AutocadRootObject>`** (new, under
`Connectors/Autocad/Speckle.Connectors.AutocadShared/Operations/Send/`; thin Civil3d/Plant3d subclasses mirroring
the v1 split). Mirror Rhino's two-phase collect-on-UI → write+upload-on-worker:
- Per used **layer** → `AddCollection(…, "Layer")` nested tier; `Solid3d` → `AddRawGeometry(SAT)` + `Solid`; other
  meshes/curves (Line/Polyline/Arc/Circle/Ellipse/Point/Mesh) → `AddGeometry` + `Display`; blocks →
  `AddDefinition`/`AddInstance`/`DisplayInstance`. Properties via `AddProperties`. Render materials →
  `AddMaterial`+`HasMaterial`; colors → `AddColor`+`HasColor`. Default scene view `[IN_COLLECTION]` (single layer tier).
- Authored **groups** (via persistent reactors) → `AddContainer(…, "Group")` + `InGroup` membership edges —
  a separate axis from `IN_COLLECTION` (an object keeps its layer AND its group(s)).
- **Deferred:** Civil3D property-set defs.

**Receive — `AutocadHostObjectArtefactBuilder : IArtifactHostObjectBuilder`** (new; Civil3D variant; **Plant3D
send-only**). Mirror the Rhino receiver, baking into AutoCAD **layers** (`GetOrCreateLayer` from `SceneViewResolver`
segments; Base-free, not v1 `AutocadLayerBaker`):
- SGEO mesh → AutoCAD mesh/entity (mirror `BuildMesh`); **SAT raw** → host entities via a new Base-free
  `Convert(byte[] sat)` overload (analogous to Rhino's `Convert3dm(byte[])`).
- Materials/colors → native; instances → block-table records + inserts per `DISPLAY_INSTANCE`.

Register both in `DependencyInjection/{AutocadConnectorModule,Civil3dConnectorModule}.cs` +
`Plant3dShared/.../Plant3dConnectorModule.cs` (send only) + `.projitems`. **Then pause and reassess before Phase 3.**

## Phase 3 — Send-only structural (AFTER the pause)

Both send-only — only a root builder + DI, no `IArtifactHostObjectBuilder`.
- **Tekla** (net48, render-mesh — closest to Rhino): `TeklaArtifactRootObjectBuilder :
  IArtifactRootObjectBuilder<TSM.ModelObject>`. Solids → SGEO `Display` meshes; Line/Arc/Polycurve/Point as needed;
  flat by-type `AddCollection` tier; render materials → `AddMaterial`+`HasMaterial`. No levels, no instances.
- **CSi/ETABS** (net48 + net8, analytical): `CsiArtifactRootObjectBuilder : IArtifactRootObjectBuilder<ICsiWrapper>`.
  Joint→Point, Frame→Line, Shell→Mesh via SGEO; Level(story)→`AddLevel`+`OnLevel`; category tier via
  `AddCollection`/eav; scene view `[ON_LEVEL → category]`. **Deferred:** section proxies, analysis-results blob.

---

## SDK building blocks to consume (do not duplicate)

- **Send:** `ObjectsArtifactPipeline` (`speckle-sharp-sdk/src/Speckle.Objects/Utils/ObjectsArtifactPipeline.cs`) —
  `InternObject`, `AddProperties(…, typeKey)`, `AddGeometry(meshAppId, Base)`, `AddRawGeometry(appId, byte[], type)`,
  `AddDefinition`, `AddInstance`, `AddMaterial`, `AddColor`, `AddLevel`, `AddCollection(key,name,parentK,subtype)`,
  `AddContainer`, relation helpers (`Display`/`Solid`/`DisplayInstance`/`Defines`/`HasMaterial`/`OnLevel`/
  `InCollection`/`InModel`/…), `AddSceneView(SceneView)` with `SceneViewKey.Rel(RelKind.X)`/`.Eav(path)`,
  `Complete()`. Upload via `IArtifactPipelineFactory.CreateInstance(...)` → `UploadFilesAsync(bundle, rootId, count)`.
  `SgeoEncoder.Encode(Base)` covers Mesh/Line/Polyline/Polycurve/Curve/Arc/Circle/Points/Ellipse/Spiral/Box.
- **Receive (Base-free):** `ArtefactBundle` (`…/Speckle.Sdk/Pipelines/Receive/Artifacts/ArtefactBundle.cs`):
  `Geometries` (index→`ArtefactGeometry{Content,Type,IsSgeo}`), `ObjectAppIds`, `Properties`, `Nodes`
  (`ArtefactNode`), `Relations` (`DisplayByObject`, `SolidByObject`, `DisplayInstanceEdges`, `DefinesByDefinition`,
  `MaterialByGeometry`, `ObjectNodeByRel`, `ObjectByGeometry()`), `Units`, `DefaultSceneView`.
  `SgeoDecoder.TryDecodeMesh(bytes, out SgeoMesh{Vertices,Faces,Colors,Units})` = Base-free mesh decode.
  `SceneViewResolver.Segments(bundle, objK)` = host-agnostic scene-view → nested-layer path.
- **Contracts (connectors repo):** `Sdk/Speckle.Connectors.Common/Builders/IArtifactRootObjectBuilder.cs` (send),
  `…/IArtifactHostObjectBuilder.cs` (receive); branch points `…/Operations/SendOperation.cs` (`SendViaArtifacts`),
  `…/Operations/ReceiveOperation.cs` (bundle + builder registered → native bake; else reconstruct; else v1).
  **DI registration is the activation switch.**

### v5 vocab quick reference
- **NodeKind:** DEFINITION=1, INSTANCE=2, MATERIAL=3, COLOR=4, LEVEL=5, CONTAINER=7 (Collection folded in via `subtype`).
- **RelKind:** DISPLAY=1, SOLID=2, SUBELEMENT=3, DEFINES=4, HAS_MATERIAL=5, HAS_COLOR=6, ON_LEVEL=7,
  DISPLAY_INSTANCE=8, DEFINES_INSTANCE=9, IN_COLLECTION=10, IN_MODEL=11, … XREF=20, CONNECTS_TO=21, HOSTED_ON=22.
- Namespace: `Speckle.Sdk.Pipelines.Send.Artifacts`.

## Reference implementations (study/mirror)

- **Rhino receive (canonical):** `Connectors/Rhino/Speckle.Connectors.RhinoShared/Operations/Receive/
  RhinoHostObjectArtefactBuilder.cs` — `BakeAll` (clean → base layer → materials → atomic geometry → instances),
  `DecodeGeometryIndex`, `ResolveLayer`, `CreateMaterials`, `BakeInstances`, `BuildTransform`, `ReceiveDiagnostics`.
- **Rhino send:** `…/Operations/Send/RhinoArtifactRootObjectBuilder.cs` (two-phase; raw 3dm + SGEO meshes; nested-layer
  COLLECTION tree; `ZstdNativeLoader.Ensure` (Speckle.Connectors.Common) net48 pre-load of `nironcompress.dll`).
- **Revit send:** `Connectors/Revit/Speckle.Connectors.RevitShared/Operations/Send/RevitArtifactRootObjectBuilder.cs`.

## Build & environment gotchas

- Build with the user-local SDK `C:\Users\oguzh\.dotnet\dotnet.exe` (10.x); PATH `dotnet` can't build net10 TFMs.
  Rhino/GH plugins are **net48**.
- Connectors reference the **local** `speckle-sharp-sdk` via `-c Local` (ProjectReference swap) — build `-c Local`.
- Locked-mode restore fast-fails with a misleading `NU1101: System.Memory` after cleaning obj/bin → fix:
  `dotnet restore <proj> -p:Configuration=Local --force-evaluate`, then `build … --no-restore`.
- ILRepack (`Speckle.Sdk.Dependencies`, `Speckle.Connectors.Logging`) double-merges on incremental builds →
  `BadImageFormatException: Duplicate type` at plugin load → clean their obj/bin + `--force-evaluate` rebuild.
- Close the host app before rebuilding (locks plugin DLLs, MSB3027). Shared-project files must be listed in `.projitems`.
- net48 native deps: deploy `nironcompress.dll` next to the plugin + `LoadLibrary`-preload it (AutoCAD 2023/24,
  Tekla, ETABS21, CSi net48).
- `TreatWarningsAsErrors=true`, strict analyzers (CA2000 etc.); `catch (Exception ex) when (!ex.IsFatal())`.
- Bundle path: `%TEMP%\speckle\receive\{versionId}\` (lowercase). Session logs: `%TEMP%\Speckle\sessions\` (capital).

## Verification (manual, per user)

- Build each target `-c Local` with the local .NET 10 SDK; confirm the plugin loads (no ILRepack/STJ failures).
- Send: confirm a `{versionId}.*.parquet` bundle is written/uploaded (`%TEMP%\Speckle\artifacts\{versionId}`);
  optionally inspect offline against the ODA/bundle-spec contract.
- Receive: confirm native geometry/layers/materials/instances bake; user performs the live host round-trips.
- Every run drops a timestamped `%TEMP%\Speckle\sessions\…ndjson` + `.summary.txt` pair (failed objects with
  appId/type/error, per-object + per-phase elapsed, bundle stats) — the primary post-mortem artefact.

---

## Status log

- **2026-07-01 (Phase 3)** — **CSi/ETABS + Tekla artefact SEND implemented & building** (send-only).
  `CsiArtifactRootObjectBuilder` (ETABS21 net48 + ETABS22 net8) and `TeklaArtifactRootObjectBuilder` (Tekla 2023/24/25
  net48). Both: DI flip in `AddCsi`/`AddTekla` + `IArtifactPipelineFactory`; two-phase threading (host COM on main,
  parquet on worker); Base-free walk (no Collection graph); net48 `Net48.IronCompress.props` zstd deploy+preload.
  CSi: `EtabsObject` DataObjects → DISPLAY Point/Line/Mesh, level→category collections (reused
  `CsiSendCollectionManager.GetCollectionSegments`); **deferred** section/material GroupProxies + the gated
  `root[analysisResults]` blob (nested element→case→station→step, not eav-shaped). Tekla: `TeklaObject` DataObjects →
  DISPLAY meshes, flat by-type, render materials via `TeklaMaterialUnpacker`, nested `elements` flattened; no analysis
  results exist. Validation manual. **All in-scope connectors now have artefact send** (Rhino/Revit/AutoCAD family/GH/
  CSi/Tekla). Remaining migration surface: native artefact **receive** for GH (bundle→wrapper) and the Csi/Tekla
  analysis-results home — both separate designs.
- **2026-07-01 (later still)** — **Grasshopper artefact SEND implemented & building** (net48 GH7/GH8).
  `GrasshopperArtifactRootObjectBuilder : IArtifactRootObjectBuilder<SpeckleCollectionWrapperGoo>` — GH already rides
  the generic `SendOperation<T>` which contains the artefact branch, so this + a `PriorityLoader` registration is the
  whole switch (no component/UX change). It walks the wrapper tree emitting `ObjectsArtifactPipeline` calls **directly,
  no `Collection` Base graph**, reusing `GrasshopperSendUnwrapper` (same clean geometry + 3dm as Rhino) and the existing
  color/material/block packers for the HAS_MATERIAL/HAS_COLOR/DEFINES/DISPLAY_INSTANCE edges. net48 zstd native
  deploy+preload added (`Net48.IronCompress.props` imported by GH7/GH8). **Receive NOT migrated** — GH outputs wrappers
  onto the canvas (not a doc bake), so artefact receive needs a bundle→wrapper reader (separate, larger). Identity note:
  cast geometry uses per-solve Guids (fine within a send; deterministic ids only needed for cross-version diffing).
  Validation is manual (user). Also: SDK branch caught up with `main` (merge `ffb8776e`) incl. #468 STJ serializer;
  connector OTEL/logo fallout fixed; ETABS/Tekla STJ aligned to 9.0.10.
- **2026-07-01 (later)** — **Phase 2 (AutoCAD family) implemented & building.** One shared
  `AutocadArtifactRootObjectBuilder` (send) + `AutocadHostObjectArtefactBuilder` (native receive) serve
  AutoCAD / Civil3D / Plant3D — registered in the shared `LoadSend`/`LoadReceive` (so AutoCAD+Civil3D get both,
  Plant3D send-only) unconditionally (SDK producer/reader build on netstandard2.0). Send reuses
  `IRootToSpeckleConverter`'s `AutocadObject` carrier transiently (display meshes + ACIS-SAT `rawEncoding`) — no
  Collection graph. Receive bakes SAT via `Body.AcisIn`, SGEO→`PolyFaceMesh`, flat layers, native materials,
  blocks for instances — all Base-free (no v1 `AutocadLayerBaker`/`AutocadMaterialBaker`/`AutocadInstanceBaker`).
  Builds clean on net48 (2024), net8 (2025/Civil3d/Plant3d), net10 (2027). **Next: PAUSE and reassess before
  Phase 3 (Tekla/CSi)** per plan. Validation is manual (user). Known first-pass simplifications: layer-level
  materials/colors skipped (object-level only); receive consumes materials, not colors.
- **2026-07-01** — Plan recovered from stuck session `03de9385` (context overflow) and written to this file.
  **Phase 0 complete & committed** (SDK `7ea929a7`, connectors `545d381b3`). **Phase 1 (Revit native receive)
  built, DI-registered, and test-run successfully** (7133 DirectShapes, 0 errors), but **3 open items**: missing
  render materials (root cause TBD — inspect bundle `f0ea106560`), "Base > Direct Shape" report label, and a
  missing Revit receive session log. **Next:** close Phase 1 items, then start Phase 2 (AutoCAD).
