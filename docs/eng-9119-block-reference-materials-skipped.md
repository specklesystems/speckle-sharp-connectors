# ENG-9119 — AutoCAD block-reference materials: not fixed in this pass

**Ticket:** [ENG-9119 — AutoCAD: preserve materials assigned to block references](https://linear.app/speckle/issue/ENG-9119/autocad-preserve-materials-assigned-to-block-references)

**Status:** left in progress, no code change. The fix cannot be completed inside
`speckle-sharp-connectors` — it needs a bundle-spec decision plus matching SDK work first.

## What the ticket asks for

Emit an object-sourced `HAS_MATERIAL` edge when a render-material proxy member resolves to an
instance K, so a material assigned directly to a `BlockReference` survives publish and its ByBlock
members inherit it. The ticket itself flags the precondition:

> Verify first that the SDK pipeline supports the equivalent of `HasColor(srcIsObject: true)`.

## Why it is blocked

It does not. `HAS_COLOR` got a source-namespace tag in ENG-8822; `HAS_MATERIAL` never did, and the
`ord` column is the only place such a tag can live.

| | `HAS_COLOR` | `HAS_MATERIAL` |
| --- | --- | --- |
| Write API | `HasColor(int srcK, int colorK, bool srcIsObject = false)` — writes `ord` 0/1 | `HasMaterial(int geometryK, int materialK)` — writes `ord` 0, always |
| Read API | `ArtefactBundle.ColorByGeometry` **and** `ColorByObject`, split on `ord` | `ArtefactBundle.MaterialByGeometry` only |

(`src/Speckle.Objects/Utils/ObjectsArtifactPipeline.cs` and
`src/Speckle.Sdk.Parquet/Pipelines/Receive/Artifacts/ArtefactBundle.cs` in `speckle-sharp-sdk`.)

An `InstanceProxy` owns no geometry, so it never enters `geometryKsByObjectId` — there is simply no
geometry K to hang the edge on, and writing the object K into the geometry slot without a namespace
tag is exactly the ambiguity ENG-8822 was raised to remove. The two K-spaces are both dense ints
from 0, so an untagged edge would silently apply the block's material to whichever unrelated
geometry happens to share that number.

The receive side is ready and needs no change: `PlaceInstances` already applies
`materialIdByObject[appId]` to the baked `BlockReference`, and `MapMaterials` already builds that
dictionary — it just never gets an entry for an instance, because no such edge exists in the bundle.

## What is needed to unblock

1. **Spec decision** (`speckle-bundle-spec`): declare `HAS_MATERIAL.src` as `geometry | object` with
   `ord` as the namespace tag (`0` = geometry, `1` = object), mirroring the `HAS_COLOR` wording. Old
   bundles all wrote `ord = 0`, so the change is backward compatible by construction — same argument
   ENG-8822 used.
2. **SDK** (`speckle-sharp-sdk`):
   - `ObjectsArtifactPipeline.HasMaterial(int srcK, int materialK, bool srcIsObject = false)`.
   - `ArtefactBundle.MaterialByObject`, populated from the `RelKind.HasMaterial` case on `ord == 1`,
     alongside the existing `MaterialByGeometry`.
   - Whatever the viewer/`ObjectsArtifactReader` path needs to resolve the object-sourced edge (the
     reader currently walks `MaterialByGeometry` only).
3. **Connector** (this repo) — small, once the above exists:
   - `AutocadArtifactRootObjectBuilder.EmitValueNodes`: in the material loop, add the
     `instanceKByObjectId` branch next to the existing geometry branch, exactly as the colour loop
     below it already does:
     `pipeline.HasMaterial(pipeline.InternObject(objectId), matK, srcIsObject: true);`
   - `AutocadHostObjectArtefactBuilder.MapMaterials`: fold `rels.MaterialByObject` into `byObject`,
     mirroring what `MapColors` does with `rels.ColorByObject`.
   - Same two edits in `RhinoArtifactRootObjectBuilder` / `RhinoHostObjectArtefactBuilder` closes the
     Rhino twin, [ENG-9109](https://linear.app/speckle/issue/ENG-9109), which is blocked on the same
     missing tag (see the note at the end of the ENG-9108 commit message, b10f446e).

## Also worth deciding at the same time

A ByLayer material on a block reference has the same shape and the same blocker. ENG-9118 (fixed in
this branch) resolves layer materials onto each inheriting object's geometry, but it explicitly
skips an inheriting block instance for this reason — the send builder now logs a warning naming the
object when that happens, rather than dropping it silently.

## Interim behaviour

Unchanged: a material assigned directly to a block reference is absent from the bundle, the placed
instance has no material, and its ByBlock members have nothing to inherit. Colour on a block
reference is unaffected — that path works, via the ENG-8822 object-sourced `HAS_COLOR` edge.
