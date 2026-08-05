# `envelope.camera_views.parquet` — named camera views / viewpoints

> **Producer-side** doc for the Speckle 4.0 camera-views artefact (per-connector extraction + mapping). Once
> implemented, the **canonical format spec** lives in the bundle-spec repo:
> `speckle-bundle-spec/spec/bundle-spec.sql` (`camera_views` table). Keep this doc in sync with that one.
>
> Status: **IMPLEMENTED (send + viewer consume + Revit native receive), 2026-08.** Decisions: forward is primary
> (target optional), `fov` is vertical degrees, Revit **linked-model views are included**, server Saved Views are
> **out of scope**. Branches: bundle-spec `camera-views-purpose-file`, SDK + connectors + sketchup
> `camera-views-artifact`, server-internal `oguzhan/camera-views-viewer`. Producers live: Rhino, Revit (host +
> linked), SketchUp. Revit native receive-side baking is done (position/direction/up/projection; no FOV/lens/crop —
> Revit's `View3D` has no setter for those). Rhino receive-side baking remains an open follow-up.

## What it is (and isn't)

A version's bundle has no home for cameras today — the artefact producer *explicitly drops* `views`/`cameras`
containers as "non-scene metadata" (`GraphArtifactProducer.cs`), so `viewer.getViews()` returns `[]` for every
bundle-loaded model. This adds one **purpose file** for **named camera viewpoints**:

```
{versionId}.envelope.camera_views.parquet
```

- **NOT `scene_views`.** `envelope.scene_views.parquet` is the scene-explorer *grouping* projection
  (Level→Category tiers). `camera_views` is 3D viewpoints (eye + direction + projection). Different features;
  same structural template (optional envelope table, buffered writer, feature-detected reader).
- **Snake_case, not kebab.** The file segment becomes the attached DuckDB view name verbatim
  (`bundleAttach.ts` / `SpecklePackfileLoader2.ts` regex `\.(?:eav|envelope)\.(.+)\.parquet$`). A kebab name
  would yield a `"camera-views"` view needing quoting everywhere and failing snake-case catalog lookups. All
  envelope tables are snake (`scene_views`, `rel_types`, `node_kinds`).
- **A dedicated fixed-column table, not a node kind.** Per bundle-spec ADR-0001 `nodes` is bounded structural
  scaffolding; a camera is a bounded scalar record → own purpose file, `required=false`, no
  `schema_version` bump (additive-optional, `structural_results` precedent).
- **Not tied to server Saved Views.** The DB-backed Saved Views feature (`viewerState.ui.camera`, world
  meters) is a separate product surface; no promotion path is designed here. Consumers convert units the same
  way they already do for geometry.

## Schema

Positions/`ortho_height` are in **model units** (the `units` column), matching bundle convention
(`nodes.units`) — the viewer scales via `getConversionFactor`, exactly as it does for geometry.
`forward`/`up` are **unit vectors** (unitless).

| column | type | meaning |
|---|---|---|
| `view` | int32 | dense ordinal, unique per row (mirrors `scene_views.view`) |
| `name` | string **null** | display label (UI shows `name ?? id`) |
| `is_default` | bool **null** | producer-nominated home/startup view (at most one true) |
| `ord` | int32 **null** | display order in menus |
| `pos_x` `pos_y` `pos_z` | float64 | camera eye position, model units |
| `forward_x` `forward_y` `forward_z` | float64 | view direction, **unit vector — required** |
| `up_x` `up_y` `up_z` | float64 | camera up, unit vector |
| `target_x` `target_y` `target_z` | float64 **null** | explicit look-at point, model units — optional (Rhino/SketchUp have one; Revit's is derived) |
| `units` | string | units of `pos`/`target`/`ortho_height` |
| `is_ortho` | bool | parallel projection flag (closes the current `Camera`-object perspective-only gap) |
| `fov` | float64 **null** | **vertical field of view in DEGREES; perspective only, null for ortho** |
| `lens_mm` | float64 **null** | 35mm-equivalent lens / focal length in mm (Rhino `Camera35mmLensLength`, SketchUp `focal_length`) |
| `ortho_height` | float64 **null** | ortho view height in model units (SketchUp `camera.height`); null for perspective |
| `aspect` | float64 **null** | frame aspect ratio, if the host has one |
| `near` `far` | float64 **null** | clipping distances, model units |

Pin the semantics as `COMMENT ON` in the spec SQL so the generated C#/TS/Python schemas + reference docs carry
them (the legacy `View3D` era standardized *no* FOV convention — don't repeat that).

**Deliberately out of scope (v1):** crop/section boxes (Revit `CropBox`), clipping planes (only legacy
Navisworks ever sent them; no viewer consumer), per-view render state (SketchUp `rendering_options`), 2D
views/sheets, server Saved Views interop. Each is an additive nullable column / sibling table later if needed.

## Per-connector mapping

| | Rhino | Revit | SketchUp (Ruby) |
|---|---|---|---|
| source | `doc.NamedViews` (`ViewInfo`/`Viewport`) | `View3D` (perspective **and** ortho; skip templates, null-Origin) | `model.pages` → `page.camera` |
| pos | `Viewport.CameraLocation` | `Origin` | `camera.eye` |
| forward/up | `CameraDirection`/`CameraUp`, unitized | `GetSavedOrientation()` | `camera.direction` / `camera.up` |
| target | `Viewport.TargetPoint` | null (derived from crop box in legacy — don't store) | `camera.target` |
| is_ortho | `IsParallelProjection` | `!IsPerspective` | `!camera.perspective?` |
| lens/fov | `Camera35mmLensLength` → `lens_mm` | — (fov derivable later) | `focal_length` → `lens_mm` (perspective) |
| ortho_height | frustum height | — | `camera.height` |

- **Rhino:** collect in `CollectOnMain` (NamedViews reads are RhinoCommon-affine → UI thread), stash on
  `CollectedModel`, emit in `WriteBundle` next to the existing `AddSceneView` call
  (`RhinoArtifactRootObjectBuilder`). Ortho views are now *included* (the v1 converter throws on them).
- **Revit:** emit in `BuildBundleSync` beside `AddSceneView` (`RevitArtifactRootObjectBuilder`).
  **Linked models included** when `SendLinkedModels` is on: collect `View3D`s from each linked
  `documentElementContext`, transform `pos`/`forward`/`up`/`target` by `documentContext.Transform` (the link
  instance total transform) into host coordinates, and prefix `name` with the link's display name
  (`LinkedModelHandler.LinkedModelDisplayNames`). A link placed by N instances yields the view N times
  (disambiguate names with the existing transform-hash suffix pattern). Host-doc views carry no transform.
- **SketchUp:** green-field — port legacy `view3d.rb::from_page` extraction into `to_speckle_v3`; add a
  `CameraView` struct to `vocab.rb` and an `add_camera_view`/`write_camera_views` pair in
  `envelope_writer.rb` mirroring the `scene_views` buffering pattern.
- **Others (AutoCAD, Tekla, CSi, …):** never sent views in any generation; opt in later by calling the same
  pipeline method.

## Producer plumbing (SDK)

Upload is filename-glob driven and count-agnostic — **no upload/manifest changes**. The additions:

1. `speckle-bundle-spec/spec/bundle-spec.sql`: `CREATE TABLE camera_views` + `COMMENT ON`s +
   `bundle_files` row (`required=false`); `npm run generate` fans out to all language schemas, docs, validator.
   No `schema_version` bump. Update `CHANGELOG.md`.
2. `speckle-sharp-sdk` `EnvelopeWriter.cs`: buffered `ParquetTableWriter` from the **generated**
   `SpecSchemas.CameraViews` (prefer generated over the hand-built schema path scene_views uses) +
   `AddCameraView(...)` + flush in `Complete()`.
3. `ObjectsArtifactPipeline.cs`: `AddCameraView` passthrough next to `AddSceneView`.

## Consumer notes

- **Attach is automatic** — both attach paths turn any `envelope.*.parquet` into a DuckDB view; they must stay
  byte-identical (`bundleAttach.ts` header warning).
- **Feature-detect before querying** (`information_schema.tables WHERE table_name='camera_views'`) — legacy
  bundles ship no such file; copy `getDefaultSceneView()` in `packfile-manager/src/bundleQueries.ts`.
- **Viewer:** inject rows in `SpecklePackfileLoader2` as tree nodes
  (`raw: { speckle_type: 'Objects.Other.Camera', position, forward, up, name, units }`) so
  `Viewer.getViews()` → `SpeckleView` → frontend-2 camera `Menu.vue` all work **with zero frontend changes**.
  The viewer applies `position + normalize(forward)` as a virtual target; projection is a separate
  `setOrthoCameraOn()`/`setPerspectiveCameraOn()` call driven by `is_ortho`.
- **frontend-3 / non-tree consumers** (Power BI visual, dashboards): read the typed `getCameraViews()` helper
  directly, like `scene_views` pivots do.
- **SDK receive:** `ArtefactBundle.CameraViews` is read by `RevitHostObjectArtefactBuilder`, which scales +
  reference-point-converts each row's eye/forward/up and hands them to `RevitViewBaker.BakeArtefactView` to create
  a perspective or orthographic `View3D`. Re-receive purges the model's previously-baked views first
  (`RevitViewBaker.PurgeArtefactViews`, same Comments-marker convention as the DirectShape bake). Not restored:
  `fov`/`lens_mm`/`ortho_height`/`aspect`/`near`/`far`/`target` — `View3D` has no setter for FOV/lens, only a crop
  box, which needs a target distance the bundle doesn't carry. Rhino receive-side baking remains an open follow-up.

## Resolved decisions (2026-07)

1. **Forward vs target** → `forward` required, `target` optional nullable.
2. **FOV convention** → vertical, degrees, perspective-only, pinned via `COMMENT ON` in the spec.
3. **Revit linked models** → include their views, transformed to host coordinates, name-prefixed, gated by
   `SendLinkedModels`.
4. **Server Saved Views** → out of scope; no unit/shape coupling.
