# The Speckle 4.0 artifact object model — a field guide

_Bundle spec · schema_version 5. Worked examples of each relation. See also: [relationships across every connector](./connector-relations.md)._

The 4.0 artifact bundle isn't a tree of nested objects like v1 `Base` — it's a **graph of flat parquet rows**, wired by typed edges across three independent ID spaces. You read an edge by knowing which space each end lives in.

## Three ID spaces, each counting from zero

A bare number is meaningless until you know its space. `obj·2`, `geo·2` and `node·2` are three unrelated things — this overlap is why the source namespace is load-bearing on every edge.

| space | lives in | what it identifies |
|---|---|---|
| **object** | `eav.objects` · `object_index` | a real source thing you drew or placed — wall, line, block placement, pipe. Identity + flattened properties. |
| **geometry** | `geometries` · `geometryIndex` | one blob — display mesh (SGEO) or lossless raw body (3dm / SAT). Content-interned. |
| **node** | `envelope.nodes` · `id` | a _synthetic_ value — material, color, level, block definition, instance, or container (layer / group / model / system). |

Reading an edge: `IN_COLLECTION: obj·0 → node·1` = "object 0 is grouped in container node 1". **The rel type fixes both namespaces**, so the same integer means different things on each side.

## The files (~14 parquet, three families)

Named `{version}.<family>.<table>.parquet`.

- **`eav.*`** — identity + flattened properties (the object space): `objects`, `paths`, `eav`, `types`, `type_eav`, `object_type`.
- **`geometries`** — the geometry space: SGEO mesh blobs + raw 3dm / SAT bodies, content-hash deduped, sharded on big models.
- **`envelope.*`** — the node space + the edges: `nodes`, `relations`, `rel_types` / `node_kinds` (self-describing catalogs), `meta`, `scene_views` / `camera_views`.

Plus the optional `eav.structural_results` purpose file.

---

## A plain object, fully assembled

A wall. Nothing is nested inside it — you reconstruct it by _following edges out of its object K_: to its mesh, to its layer, and the mesh onward to its material.

```mermaid
graph LR
  W([obj·0 · Wall W1]):::obj
  G[geo·0 · mesh]:::geo
  MAT{{node·0 · MATERIAL}}:::nd
  LAY{{node·1 · CONTAINER Layer}}:::nd
  W -->|DISPLAY 1| G
  G -->|HAS_MATERIAL 5| MAT
  W -->|IN_COLLECTION 10| LAY
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef geo fill:#5fc7b8,stroke:#0a7369,color:#052723;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

**Render loop:** for each object, walk `DISPLAY` to its meshes, each mesh's `HAS_MATERIAL` to its appearance, and `IN_COLLECTION` to its layer. No containment — pure edge-following.

## Materials — shared once, pointed at many times

Two walls with the same grey material. The material is _one_ node; both meshes point at it — the dedup.

```mermaid
graph LR
  W1([obj·0 · Wall A]):::obj
  W2([obj·1 · Wall B]):::obj
  G1[geo·0 · mesh A]:::geo
  G2[geo·1 · mesh B]:::geo
  MAT{{node·0 · MATERIAL grey}}:::nd
  W1 -->|DISPLAY| G1
  W2 -->|DISPLAY| G2
  G1 -->|HAS_MATERIAL| MAT
  G2 -->|HAS_MATERIAL| MAT
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef geo fill:#5fc7b8,stroke:#0a7369,color:#052723;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

`HAS_MATERIAL` flows _geometry → node_, not object → node. Material binds to the **mesh**, so a definition's shared geometry carries its material into every placement automatically.

## Colors — the one edge with two source spaces

A by-object display color binds to _geometry_. But a block _placement_ owns no geometry of its own, so its instance color binds to the _object_. Same rel, two source namespaces — the edge's `ord` column tags which (0 = geometry, 1 = object).

```mermaid
graph LR
  L[geo·0 · line mesh]:::geo
  R([obj·5 · block placement]):::obj
  C1{{node·0 · COLOR red}}:::nd
  C2{{node·1 · COLOR blue}}:::nd
  L -->|HAS_COLOR · ord 0| C1
  R -->|HAS_COLOR · ord 1| C2
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef geo fill:#5fc7b8,stroke:#0a7369,color:#052723;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

Without the `ord` tag, `obj·5` and `geo·5` are indistinguishable and the color lands on the wrong element (ENG-8822). COLOR is a distinct viewer render mode — an object can carry a full material _and_ a color override at once.

## Levels — a storey is a node, membership is an edge

Revit. The level is a LEVEL node with `elevation`; every object on it points there.

```mermaid
graph LR
  W([obj·0 · Wall]):::obj
  D([obj·1 · Door]):::obj
  L1{{node·0 · LEVEL · elev 0}}:::nd
  L2{{node·1 · LEVEL · elev 3000}}:::nd
  W -->|ON_LEVEL| L1
  D -->|ON_LEVEL| L2
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

**Grouping is projected, not baked.** The scene tree "Level → Category → Family" isn't stored as folders — it's a recipe in `scene_views`: group by the ON_LEVEL edge, then by the `category` / `family` eav properties. Only the Level tier is a relation.

## Nested layers — membership is an edge, nesting is a column

Rhino, where layers nest. The **object → its layer** is the `IN_COLLECTION` edge; the **layer → its parent layer** is the `def_ref` pointer _on the container node itself_.

```mermaid
graph TB
  BLD{{node·0 · CONTAINER · Building}}:::nd
  WAL{{node·1 · CONTAINER · Walls}}:::nd
  A([obj·0 · slab]):::obj
  B([obj·1 · wall]):::obj
  WAL -.->|def_ref| BLD
  A -->|IN_COLLECTION| BLD
  B -->|IN_COLLECTION| WAL
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

A layer isn't its own node kind. It's a CONTAINER with `subtype = "Layer"` — the same node kind that carries groups, models and systems, differing only by subtype. AutoCAD's flat layers are just containers with `def_ref = null`.

## Blocks & instances — geometry owned once, placed many times

A DEFINITION owns the shared geometry; each placement is an INSTANCE node carrying a transform and pointing back at the definition.

```mermaid
graph LR
  R([obj·2 · placement R]):::obj
  INST{{node·1 · INSTANCE · T}}:::nd
  DEF{{node·0 · DEFINITION Frame}}:::nd
  GA[geo·0 · line]:::geo
  GB[geo·1 · rect]:::geo
  R -->|DISPLAY_INSTANCE| INST
  INST -.->|def_ref| DEF
  DEF -->|DEFINES · ord 0| GA
  DEF -->|DEFINES · ord 1| GB
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef geo fill:#5fc7b8,stroke:#0a7369,color:#052723;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

Members have **no `DISPLAY` / `DISPLAY_INSTANCE`** — deliberate suppression, otherwise they'd draw untransformed at the origin (ENG-8782). A placement's transform composes onto the definition's geometry at receive. `DEFINES_INSTANCE` (node → node) handles a block nested inside another block. A member does keep its ordinary object row and `IN_COLLECTION`; see [member layers](#member-layers--a-carrier-row-and-a-stamp).

## Groups — a second, overlapping axis

A group is a CONTAINER with `subtype = "Group"`. Unlike a layer, this axis _overlaps_: an object keeps its layer AND sits in one or more groups.

```mermaid
graph LR
  A([obj·0 · line]):::obj
  B([obj·1 · arc]):::obj
  LAY{{node·0 · CONTAINER Layer}}:::nd
  GRP{{node·1 · CONTAINER Group}}:::nd
  A -->|IN_COLLECTION| LAY
  B -->|IN_COLLECTION| LAY
  A -->|IN_GROUP| GRP
  B -->|IN_GROUP| GRP
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

The receiver stores `IN_COLLECTION` last-wins — it _is_ the scene tree, single-valued. Groups are a separate multi-valued axis, so they needed their own rel.

## Solids — two geometries for one object

A Brep / Solid3d ships _both_ a lossless raw body (same-host round trip) and a tessellated display mesh (viewer / foreign hosts).

```mermaid
graph LR
  S([obj·0 · Solid3d]):::obj
  RAW[geo·0 · SAT body]:::geo
  MSH[geo·1 · display mesh]:::geo
  S -->|SOLID 2| RAW
  S -->|DISPLAY 1| MSH
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef geo fill:#5fc7b8,stroke:#0a7369,color:#052723;
```

Receive prefers the solid, falls back to the mesh — a Rhino model received into AutoCAD can't read a foreign 3dm, so it uses the DISPLAY mesh (ENG-8820).

## Topology — edges between real objects

Not everything routes through a synthetic node. A whole family connects _object to object_ directly.

```mermaid
graph LR
  RAIL([railing]):::obj
  BAL([baluster]):::obj
  WIN([window]):::obj
  WALL([wall]):::obj
  ROOM([room]):::obj
  P1([pipe]):::obj
  P2([pipe]):::obj
  RAIL -->|SUBELEMENT 3| BAL
  WIN -->|HOSTED_ON 22| WALL
  WALL -->|BOUNDS 23| ROOM
  WIN -->|IN_ROOM 12| ROOM
  P1 -->|CONNECTS_TO 21| P2
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
```

Rooms are _objects_, not nodes — so `IN_ROOM` and `BOUNDS` point at an object K, in contrast with `IN_SYSTEM`/`IN_GROUP` which point at synthetic container nodes.

---

## Member layers — a carrier row and a stamp

A definition member is _reachable from its definition_ only as a **geometry** K (`DEFINES`) or, if it is itself a nested placement, as an **INSTANCE node** K (`DEFINES_INSTANCE`). It has no `DISPLAY` edge, so `ObjectByGeometry()` — built from `DISPLAY` alone — cannot invert either one. That was the gap: whatever the member's object row held, nothing could find it.

The fix needs no new relation. The member keeps a **carrier object row** with the same object-sourced `IN_COLLECTION` every top-level object emits, and an **eav stamp** supplies the missing direction back to it — `@speckle.geometry_k` (or `@speckle.instance_k` for a nested member). SketchUp did this first for tags (ENG-8851); Rhino reuses the same keys (ENG-9110).

```mermaid
graph LR
  R([obj·0 · placement]):::obj
  INST{{node·2 · INSTANCE}}:::nd
  DEF{{node·3 · DEFINITION}}:::nd
  GA[geo·0 · member mesh]:::geo
  M([obj·1 · member carrier]):::obj
  LA{{node·0 · CONTAINER Layer A}}:::nd
  LB{{node·1 · CONTAINER Layer B}}:::nd
  R -->|DISPLAY_INSTANCE| INST
  INST -.->|def_ref| DEF
  DEF -->|DEFINES| GA
  R -->|IN_COLLECTION| LA
  M -->|IN_COLLECTION| LB
  M -.->|"eav @speckle.geometry_k"| GA
  classDef obj fill:#eac36a,stroke:#9a5f0c,color:#2a1c04;
  classDef geo fill:#5fc7b8,stroke:#0a7369,color:#052723;
  classDef nd fill:#b3a8f0,stroke:#5647bd,color:#211648;
```

The placement sits on Layer A; the member drawn through it belongs to Layer B. Receive walks `DEFINES` to `geo·0`, the stamp back to `obj·1`, then that carrier's ordinary `IN_COLLECTION` — no special-case projection anywhere.

**The invariant this rests on:** a carrier has no render edge, so every consumer that walks objects must skip render-less ones. Both C# receive paths already do (the Rhino native builder gates on `DISPLAY`/`SOLID`/`DISPLAY_INSTANCE`; `ObjectsArtifactReader` drops an object whose geometry build returns null). Break that and members bake twice — and show up in the scene explorer.

Two wrinkles worth knowing. A Rhino member owns **several** geometry Ks (a lossless 3dm solid _and_ its display mesh, chosen between per member), so the stamp is a comma-joined list where SketchUp writes a bare integer — and eav coerces a numeric-looking single value, so a one-K stamp reads back as a `double`. Any reader accepts both. And because the carrier is a real object row, member **properties** are now reachable by the same route — the piece that used to be listed as a separate gap.

---

_The model in one line: **flat rows in three ID spaces, wired by typed edges whose namespaces are fixed.** Understanding = knowing which space each number is in and following the edges._
