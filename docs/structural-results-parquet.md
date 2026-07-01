# `eav.structural-results.parquet` — structural analysis/design results

> Producer-side design doc for the Speckle 4.0 structural analysis-results artefact. Written for review with a
> structural engineer — see **Open questions** at the end. Status: **4 of 8 ETABS result types live; 4 pending this
> review.** Schema is a persisted parquet format — additive nullable columns are safe to add later; retyping/removing
> is not, so we want the axes right.

## What it is (and isn't)

A version's artefact bundle already has `geometries.parquet` (SGEO shapes), `eav.*.parquet` (per-object properties),
and `envelope.*.parquet` (topology graph). This adds one more **purpose file** for **structural analysis + design
results**:

```
{versionId}.eav.structural-results.parquet
```

- **Per-domain, not per-connector.** ETABS/CSi, SAP2000, and **TSD** (Tekla Structural Designer, coming soon) all
  write this **same** schema. A future *non-structural* domain (environmental/daylight/energy from Grasshopper,
  thermal, …) gets its **own** `eav.{domain}-results.parquet` — because those have different axes (hour, weather,
  metric) and jamming them here would mean per-domain column sprawl. So the rule is: **one file per analysis domain,
  shared by every connector in that domain.**
- **Not** folded into `eav.eav.parquet`. Results have extra axes (load case, station, step) that `eav`'s single
  `(object, path, value)` triple can't hold without baking them into the property path — which explodes the shared
  path dictionary and makes results unqueryable. Here those axes are **typed columns**, so the eav path dictionary
  stays small and results stay queryable/aggregatable.
- **Send-only today.** No receiver reads it yet; consumers (viewer, dashboards, a future receive) add a reader when
  they consume results. Every value is a leaf row (long/tidy format).

## Schema

**Live columns** (`StructuralResultsWriter`, `speckle-sharp-sdk`):

| column | type | meaning |
|---|---|---|
| `object_index` | int32 **null** | → `eav.objects` (the SAME dense K the member/joint was interned with). **Set = object-level; null = model-level.** |
| `location` | string (dict) **null** | model-level identity when `object_index` is null (story name, or blank for whole-model) |
| `result_type` | string (dict) | `frameForce` · `jointReaction` · `baseReaction` · `modalPeriod` · (proposed) `pierForce` · `spandrelForce` · `storyDrift` · `storyForce` · (TSD) `memberForce` · `utilization` |
| `load_case` | string (dict) | load case / combo / mode name (`Dead`, `EQx`, `Modal`) |
| `component` | string (dict) | the quantity: `P` `V2` `V3` `T` `M2` `M3` / `F1..M3` / `Period` / `drift` / … |
| `station` | float64 **null** | numeric position along a member (frame `ElmSta`); null for point/model results |
| `step` | int32 **null** | time-history step / mode index; null or 1 for a static case |
| `value` | float64 **null** | the numeric result |
| `value_text` | string **null** | non-numeric design output (`PASS`/`FAIL`); null for analysis results |

**Proposed additions** (needed for the 4 pending types — both nullable, additive/safe; live rows carry null):

| column | type | meaning |
|---|---|---|
| `element_name` | string (dict) **null** | the result's element identity when it is **not** an interned object — pier/spandrel name, or an analysis-only sub-element name |
| `position_label` | string (dict) **null** | a **categorical** position/direction that isn't a numeric station: pier/spandrel `Location` (`Top`/`Bottom`), story-drift `Direction` (`X`/`Y`) |

## Object-level vs model-level

The correlation back to geometry is `object_index` → `eav.objects.application_id` → the member/joint/shell you sent.

- **Object-level** (frame forces, joint reactions): `object_index` set (via a *name → applicationId* map, because CSi
  keys results by element **name** while objects are interned by **applicationId**), `location` null.
- **Model-level** (story drift, story force, modal period, base reaction): `object_index` null; identity is `location`
  (story) and/or `step` (mode), or blank for whole-model (base reaction).

## The 8 ETABS result types → schema

**✅ Live (map cleanly — all-numeric leaves, single identity):**

| result_type | ETABS grouping keys | mapping |
|---|---|---|
| `frameForce` | `Elm, LoadCase, ElmSta, StepNum` | `object`=Elm→appId · `load_case` · `station`=ElmSta · `step`=StepNum · `component`∈{P,V2,V3,T,M2,M3} |
| `jointReaction` | `Elm, LoadCase, StepNum` | `object`=Elm→appId · `load_case` · `step` · `component`∈{F1,F2,F3,M1,M2,M3} |
| `baseReaction` | `LoadCase, StepNum` | model-level · `load_case` · `step` · `component`∈{FX,FY,FZ,MX,MY,MZ} |
| `modalPeriod` | `LoadCase, Mode` | model-level · `load_case`=`Modal` · `step`=Mode · `component`∈{Period,Frequency,CircFreq,Eigenvalue} |

**⏳ Pending review (need the proposed columns and/or a rule):**

| result_type | ETABS grouping keys / result keys | why it doesn't fit yet | proposed mapping |
|---|---|---|---|
| `pierForce` | keys: `PierName, StoryName, LoadCase, Location`; values: P,V2,V3,T,M2,M3 | **dual identity** (Pier + Story) + `Location` is a **string** (`Top`/`Bottom`), not a numeric station; **piers aren't interned objects** (they're named groups of shells) | `object_index`=null · `element_name`=Pier · `location`=Story · `position_label`=Location · `component`∈{P..M3} |
| `spandrelForce` | keys: `SpandrelName, StoryName, LoadCase, Location`; values: P..M3 | same as piers | `element_name`=Spandrel · `location`=Story · `position_label`=Location · `component`∈{P..M3} |
| `storyDrift` | keys: `Story, LoadCase, StepNum`; values: **`Direction, Drift, Label, X, Y, Z`** | **mixed result keys**: `Drift` is the value, `Direction`(X/Y) is really a *dimension*, `Label`/`X`/`Y`/`Z` are locating metadata (not results) | `location`=Story · `component`=`drift` · `position_label`=Direction · `value`=Drift — **and DROP `Label`/`X`/`Y`/`Z`** ⚠️ |
| `storyForce` | keys: `Story, LoadCase, Location`; values: axial/major-shear/minor-shear/torsion/major-moment/minor-moment | `Location` is a string story position | `location`=Story · `position_label`=Location · `component`∈{axial,majorShear,…} |

## Why axes-as-columns (not property paths)

Baking `case/station/step` into a property path (`frameForces.EQx.sta_1.5.step_237.M3`) makes nearly every path unique
— for a 5,000-member model with a time-history that's **billions** of distinct path-dictionary entries, and you can't
query it without string-parsing. As typed columns, a value is one tidy row and "max M3 per column under EQx" is a
normal `GROUP BY`. (Full comparison lived in the design chat; summary: keep the axes as data.)

## Gating

Results are **opt-in**: only produced when the user selected load cases/combos **and** result types in the publish
settings, and only if the model is **locked** (analysis has been run). A results-extraction failure (unlocked model /
case not finished) is **logged and skipped** so the geometry + properties still send.

## Consumer notes (querying)

- Join object-level results to geometry/properties: `structural-results.object_index = eav.objects.object_index`.
- Model-level rows have `object_index IS NULL`; group by `location` (story) / `step` (mode).
- `result_type` / `load_case` / `component` are dictionary-encoded → cheap to filter/group.
- Numeric result = `value`; design verdicts = `value_text` (TSD).

---

## Open questions for the structural engineer

1. **Story drifts — drop the locating metadata?** ETABS returns `Direction, Drift, Label, X, Y, Z` per row. Plan: keep
   **`Drift` per `Direction`** (X/Y) and **drop `Label` and the `X/Y/Z` coordinates**. Are `Label` / `X/Y/Z` needed
   downstream (e.g. to locate the drift point), or is drift-per-direction-per-story-per-case enough?
2. **Piers & spandrels — identity.** A pier is identified by **(PierName, StoryName)** and isn't an interned object.
   Plan: `element_name`=PierName, `location`=StoryName, `object_index`=null. Is that the right key, or should piers be
   promoted to first-class objects (interned, with geometry) so results attach to `object_index` like frames?
3. **`Location` = Top/Bottom (piers/spandrels/story forces).** Plan: a categorical `position_label` column. Correct, or
   is Top/Bottom better modelled another way (e.g. two rows with a numeric station 0/1)?
4. **Analysis vs drawn elements (`Elm`).** ETABS forces are keyed by the *analysis* element name, which can differ from
   the drawn member when a member is meshed into several analysis elements. Currently: if `Elm` matches a sent object's
   name → `object_index`; else → `element_name` (name kept, no object join). Is per-analysis-element granularity
   wanted, or should sub-element results be aggregated back onto the drawn member?
5. **Which result types matter?** We ship frame forces, joint reactions, base reactions, modal periods today. Are
   pier/spandrel/story results high-value, or rarely consumed? (Drives whether to finalise the pending 4 now.)
6. **Units.** Force/length/temperature units are on the version root (`forceUnits`, etc.); results values are in those
   model units. Do we need per-result units, or is the model-wide unit set sufficient?
7. **Envelopes vs full arrays.** Time-history/modal produce many `step` rows. Do consumers mostly want **enveloped**
   results (max/min per member per combo) — which we could *also* surface compactly — or the full step arrays?
