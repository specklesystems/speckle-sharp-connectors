# Connector topology — envelope relations per connector

> How each 4.0 connector maps host-application topology (hosting, containment, connectivity, systems,
> rooms, levels) onto the bundle **envelope graph** (`nodes` + typed `relations`). The relation/node
> vocabulary is the single source of truth in `speckle-bundle-spec/spec/bundle-spec.sql`; the managed
> producer façade is `ObjectsArtifactPipeline` (`speckle-sharp-sdk/src/Speckle.Objects/Utils`). The native
> ODA C++ producers in `speckle-oda/native/{rvextract,nwextract}` are the reference blueprint. Status:
> **rolling out in phases** — see the status column.

## The vocabulary (live relations)

| rel | src → dst | ord | meaning | producer façade |
|---|---|---|---|---|
| DISPLAY (1) | object → geometry | ordinal | object's own mesh | `Display` |
| SOLID (2) | object → geometry | ordinal | lossless solid blob | `Solid` |
| SUBELEMENT (3) | object → object | ordinal | parent → child (host/nested) | `Subelement` |
| DEFINES (4) | node → geometry | — | DEFINITION → mesh | `Defines` |
| HAS_MATERIAL (5) | geometry → node | — | mesh → MATERIAL | `HasMaterial` |
| HAS_COLOR (6) | geo\|obj → node | — | colour override | `HasColor` |
| ON_LEVEL (7) | object → node | — | element → LEVEL | `OnLevel` |
| DISPLAY_INSTANCE (8) | object → node | ordinal | placement of a definition | `DisplayInstance` |
| DEFINES_INSTANCE (9) | node → node | ordinal | nested instancing | `DefinesInstance` |
| IN_COLLECTION (10) | object → node | — | scene-tree membership | `InCollection` / `AddCollection` |
| IN_MODEL (11) | object → node | — | source/linked model | `InModel` |
| IN_ROOM (12) | **object → object** | — | element → room object | `InRoom` |
| IN_SYSTEM (14) | object → node | — | MEP System / Network | `InSystem` + `AddContainer(…,"MEP System"\|"Network")` |
| CONNECTS_TO (21) | object → object | **scope** | connectivity (ord = system-K / opening-K / 0) | `ConnectsTo(s,t[,scope])` |
| BOUNDS (23) | object → object | — | bounding wall → room | `Bounds` |

Node kinds: DEFINITION, INSTANCE, MATERIAL, COLOR, LEVEL, CONTAINER (subtype `Collection｜Model｜MEP System｜Network`).
**Retired — do NOT use:** IN_SPACE(13), IN_NETWORK(15), IN_LINE(16), **IN_GROUP(17)**, IN_ASSEMBLY(18),
IN_SUBASSEMBLY(19), XREF(20), HOSTED_ON(22), COLLECTION-node(6).

**`CONNECTS_TO.ord` is a scope, not an order** (`rel_types.ord_semantics='scope'`): system-K for MEP flow,
opening-K for room adjacency, 0 unscoped. The façade `ConnectsTo(s,t,scope)` overload carries it.

## Per-connector map (✅ live · 🔶 deferred follow-up · ⛔ blocked-on-decision)

### Revit — `RevitArtifactRootObjectBuilder`
- ✅ IN_MODEL, DISPLAY, DISPLAY_INSTANCE, DEFINES, HAS_MATERIAL, ON_LEVEL (pre-existing).
- ✅ **SUBELEMENT** — (a) `RevitObject.elements` nested children (curtain wall→mullions/panels, railing→top
  rail, stacked wall→members) — these are stripped from the atomic list by
  `RemoveKnownChildElementsWhenParentPresent`, so the lift also **recovered dropped child geometry**;
  (b) host/super-component → element (`FamilyInstance.Host ?? .SuperComponent`).
- ✅ **IN_ROOM** — `FamilyInstance.Room ?? .Space` → room object.
- ✅ **CONNECTS_TO (spatial)** — door/window `FromRoom → ToRoom`, scope = opening object K.
- 🔶 **BOUNDS** — `Room.GetBoundarySegments → BoundarySegment.ElementId` (new API walk; the Areas pattern
  exists in `DisplayValueExtractor`). Rooms must be sent.
- 🔶 **IN_SYSTEM** — `MEPCurve.MEPSystem` / `FamilyInstance.MEPModel.ConnectorManager…MEPSystem`
  (`ParameterExtractor.GetMEPSystem` resolves it as a property today; needs a system CONTAINER + edge).
- 🔶 **CONNECTS_TO (MEP)** — `ConnectorManager.Connectors` connected pairs, scope = system K (new walk).
- 🔶 **Assemblies** — `AssemblyInstance.GetMemberIds` → SUBELEMENT (new walk).

### Rhino / Grasshopper — `RhinoArtifactRootObjectBuilder`, `GrasshopperArtifactRootObjectBuilder`
- ✅ IN_COLLECTION (layer tree), SOLID, DISPLAY, DISPLAY_INSTANCE, DEFINES/DEFINES_INSTANCE, HAS_MATERIAL
  (GH also HAS_COLOR). GH is the most complete builder.
- 🔶 Rhino **HAS_COLOR** — object display colours (GH already emits it).
- ⛔ **Groups** — `RhinoGroupUnpacker` exists but grouping has no clean live relation (see decision below).

### AutoCAD / Civil3D / Plant3D — `AutocadArtifactRootObjectBuilder` (one builder, three verticals)
- ✅ IN_COLLECTION (layers), SOLID (ACIS-SAT), DISPLAY, DISPLAY_INSTANCE, DEFINES/_INSTANCE, HAS_MATERIAL,
  HAS_COLOR.
- ✅ **Civil3D SUBELEMENT** — `Civil3dObject.elements` tree (corridor→baseline→region→assembly→subassembly;
  alignment→profiles; site→parcels/feature-lines), guarded against double-emit.
- ✅ **Civil3D IN_SYSTEM** — part → pipe network (CONTAINER subtype `Network`), from the resolved
  `Assignments.networkId`.
- ✅ **Civil3D CONNECTS_TO** — pipe → start/end structure (`Assignments.startStructureId/endStructureId`),
  guarded to sent objects.
- 🔶 **Plant3D IN_SYSTEM** — component → line-number / service (`Plant3dDataExtractor` PnP row); the
  line-number field is the grouping key (new: read the PnP row + CONTAINER).
- 🔶 **Plant3D CONNECTS_TO** — pipe↔fitting port connectivity via the PnP `Port` API (new walk; not
  extracted anywhere today).
- ⛔ **AutoCAD groups** — `AutocadGroupUnpacker` exists; same grouping blocker as Rhino.

### CSi / ETABS / SAP2000 — `CsiArtifactRootObjectBuilder`
- ✅ IN_COLLECTION (by-type / Level→Category tree), DISPLAY, structural_results (separate purpose file).
- ✅ **CONNECTS_TO** — frame → I-/J-end joint objects (`Geometry["I-End Joint"|"J-End Joint"]` via
  `nameToAppId`). The slab↔beam↔column graph through shared joints.
- 🔶 **ON_LEVEL** — story from `GetLabelAndLevel` (currently only a collection segment) → LEVEL node + edge.
- 🔶 **IN_SYSTEM / grouping** — `GetGroupAssign` groups + pier/spandrel/diaphragm
  (`EtabsShellPropertiesExtractor`) → semantic CONTAINERs. (Aligns with "pier = named group of walls".)
- 🔶 **HAS_MATERIAL / section** — `CsiToSpeckleCacheSingleton` section/material caches (two-tier proxy).

### Tekla — `TeklaArtifactRootObjectBuilder`
- ✅ IN_COLLECTION (flat by type), DISPLAY, HAS_MATERIAL.
- ✅ **SUBELEMENT** — `TeklaObject.elements` (part → bolts / rebar / sub-parts; was flattened).
- 🔶 **Assemblies** — `Part.GetAssembly()` / `Assembly.GetMainPart()` / `GetSecondaries()` → SUBELEMENT
  main→secondary (new walk; not extracted today).
- 🔶 **CONNECTS_TO** — `Weld.MainObject / SecondaryObject` (welds are excluded from children today) → part
  ↔ part connection (new walk).

## ODA blueprint (how the native producers derive each relation)

The managed connectors have the same host-API affordances; the ODA extractors are the working reference.

| rel | rvextract (Revit / BimRv) | nwextract (Navis) |
|---|---|---|
| SUBELEMENT | `OdBmElement::owningElemId()` (≈ `Element.GetHostId`) | — |
| ON_LEVEL | `getAssocLevelId()` + level params → `addLevelNode` | "Level" property group |
| IN_ROOM | `OdBmFamilyInstance::getRoomId(Room)` | — |
| BOUNDS | `OdBmRoomElem::getBoundarySegments()` → segment `getElementId()` | — |
| CONNECTS_TO | MEP: `getBaseConnectorManager→getConnectors→getRefs/getDirection`, ord=system-K; spatial: `getRoomId(From/To)`, ord=opening-K | IFC port reconstruction (coincident `IfcDistributionPort` + FlowDirection), ord=0 |
| IN_SYSTEM | `getMEPSystem()` → CONTAINER subtype "MEP System" | network components → CONTAINER subtype "Network" |
| DISPLAY_INSTANCE / DEFINES / DEFINES_INSTANCE | `OdBmGElement` geometry-node walk | fragment grouping (no DEFINES_INSTANCE) |
| IN_MODEL | — (single native model) | `OdNwPartition::getSourceFileName()` → CONTAINER subtype "Model" |

## Modeling decisions

1. **Grouping needs a relation reintroduction (⛔ blocked).** The only live obj→CONTAINER "grouping"
   relation is `IN_COLLECTION`, and the receive side stores it **last-wins single-valued**
   (`ArtefactRelations.CollectionByObject[src]=dst`) — it *is* the scene tree. Reusing it for groups would
   make members land in the group **instead of** their layer/type collection, regressing the scene tree and
   .NET round-trip receive. The clean home is the **retired `IN_GROUP` (17)** — reintroducing it is a
   **shared bundle-format change** (spec + regenerate + façade + receive handling + viewer/server support),
   so it is deferred to a team decision rather than shipped blind. Affects Rhino/AutoCAD scene groups and
   the semantic-grouping variants (CSi groups). (CSi pier/spandrel and Civil networks use `IN_SYSTEM`, which
   is a *distinct* rel/map and does not conflict.)
2. **Host / hosted uses SUBELEMENT** (object→object), not the retired `HOSTED_ON(22)`.
3. **Civil pipe networks use CONTAINER subtype `Network`**; Revit MEP systems use `MEP System`.
4. **Assemblies (Tekla) use SUBELEMENT** (main → secondary), avoiding a new CONTAINER subtype.

## Implementation status

- **Phase 0** (SDK) — ✅ `Bounds` façade + scoped `ConnectsTo(…,scope)` overload + `InRoom` doc fix.
- **Phase 1** (SUBELEMENT lift) — ✅ Tekla, Revit (recovers dropped child geometry), Civil3D.
- **Phase 3** (Revit) — ✅ IN_ROOM, host SUBELEMENT, spatial CONNECTS_TO. 🔶 BOUNDS / MEP IN_SYSTEM+connectivity / assemblies.
- **Phase 4** (Civil3D) — ✅ network IN_SYSTEM, pipe→structure CONNECTS_TO. 🔶 Plant3D IN_SYSTEM + ports.
- **Phase 5** (CSi) — ✅ member↔joint CONNECTS_TO. 🔶 ON_LEVEL / pier-spandrel IN_SYSTEM / section HAS_MATERIAL; Tekla assemblies + welds.
- **Phase 2** (grouping) — ⛔ blocked on the `IN_GROUP` decision above.

The 🔶 items all require **new host-API walks in the collect phase** (main-thread Revit/Plant3D/Tekla API);
they were left as a **tested** follow-up rather than shipped blind, and each is wrapped/guarded so it can
never fail the geometry send.

## Verification

Send from the host app, then query the bundle with the DuckDB CLI (as for structural results):

```sql
SELECT r.name, count(*) FROM read_parquet('{v}.envelope.relations.parquet') rel
JOIN read_parquet('{v}.envelope.rel_types.parquet') r ON r.id = rel.rel GROUP BY 1 ORDER BY 2 DESC;
```

Spot-check that `src`/`dst` resolve to real `objects.object_index` (obj-ns rels) or `nodes.id` (node-ns
rels) per the rel's namespaces. For SUBELEMENT specifically, confirm child geometry now appears (Revit
curtain walls / Tekla parts). See `docs/structural-results-parquet.md` for the DuckDB inspection pattern.
