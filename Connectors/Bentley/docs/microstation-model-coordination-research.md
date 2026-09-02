# MicroStation / OpenRoads model coordination — deep research for a Speckle "reference point" setting

**Date:** 2026-09-02 · **Branch:** `david/microstation-prototype-1` · **Status:** research, no code changes.

**Goal.** Revit's connector offers a *Reference Point* send/receive setting (Internal Origin / Project Base
Point / Survey Point / Shared Coordinates). This document collects everything needed to build the same
feature for MicroStation and OpenRoads: what coordinate/datum concepts a DGN model has, where they are
stored, how to read and write them through the Bentley managed C# API (`Bentley.DgnPlatformNET`,
`Bentley.DgnGeoCoord2`, COM interop) and the ODA Drawings C++ API (`dgnextract`), how the Revit
implementation in this repo works, and a recommended design.

Research inputs: this repo (Revit + MicroStation connectors), `C:\dev\speckle-converters` (dgnextract +
ODA 27.5 SDK headers/examples), reflection over the installed MicroStation 2026 / OpenRoads 2025 managed
assemblies, the MicroStation Python API stubs (1:1 mirror of the C++ DgnPlatform doc comments), the
decompiled `MicroStationVBA.chm`, Bentley help/KB/Communities, iTwin.js docs, DOT tech notes, ODA docs.
Everything labelled **[verified]** was read from code, reflection, or shipped docs; **[web]** comes from
online sources; **[unverified]** must be confirmed on a live MicroStation (the `Speckle probe` keyin is the
natural place).

---

## Table of contents

1. Executive summary and Revit ↔ MicroStation mapping
2. MicroStation coordinate concepts in depth
   - 2.1 Design plane, UOR, working units, Solids Working Area
   - 2.2 Global Origin
   - 2.3 Auxiliary Coordinate Systems (ACS)
   - 2.4 Geographic Coordinate System (GCS)
   - 2.5 North: DEFINE NORTH / azimuth, angle readout, solar, grid convergence
   - 2.6 Grid, views, view rotation
   - 2.7 Reference attachments and their transform
   - 2.8 OpenRoads / OpenRail / OpenBridge / OpenBuildings specifics
   - 2.9 Bentley's own mapping: DGN → iModel (globalOrigin / ecefLocation / GCS)
3. How the Revit connector implements reference points (the pattern to copy)
4. Bentley managed C# API — read and write, per concept
5. ODA Drawings C++ API — read and write, per concept
6. Current state of our code (C# connector and dgnextract) and gaps
7. Recommended design for the MicroStation reference point feature
8. Verification checklist (probe keyin) and open questions
9. Sources

---

## 1. Executive summary and Revit ↔ MicroStation mapping

MicroStation has **no fixed trio of named points** like Revit. It has a layered coordinate model:

| Layer | What it is | Moves geometry? | Per |
|---|---|---|---|
| **Design plane / design cube** | The storage frame. Coordinates are stored as doubles in **UORs** (units of resolution). UOR (0,0,0) is the cube centre. Immovable. | — | model |
| **Global Origin (GO)** | An offset (in UORs) subtracted from every stored coordinate for input/readout. `readout = (UOR − GO) / uorPerMaster`. Translation only, no rotation. | No, re-labels | model |
| **Working units** | Storage unit + resolution (UOR per storage unit, default 10 000 UOR/m), master unit, sub unit. | Changing resolution rescales geometry | model |
| **ACS** (Auxiliary Coordinate System) | Named local Cartesian/cylindrical/spherical frames with origin (UOR), rotation matrix, scale. One *active* ACS per view. Input/readout aid (like AutoCAD UCS). | No | model (named), view (active) |
| **GCS** (Geographic Coordinate System) | A full CS-Map projected CRS + datum + ellipsoid + vertical datum + optional **Helmert local transform** (scale/rotation/offset). Model X/Y are Easting/Northing in CRS units (after GO and Helmert). Stored as a control element in the model. | Only if the user chooses "Reproject" | model (primary + "reference" slot) |
| **Azimuth / DEFINE NORTH** | Per-model true-north angle used for direction readout and solar lighting. | No | model |
| **Reference attachments** | Master origin, reference origin, rotation, scale, orientation mode (Coincident / Coincident-World / Geographic). | Placement of the linked model | attachment |

### Mapping table

| Revit | MicroStation / OpenRoads | Notes |
|---|---|---|
| **Internal Origin** (fixed storage frame) | **Design-plane origin**, UOR (0,0,0), also the Solids Working Area centre | Raw DgnPlatformNET / ODA coordinates live here. Immovable. |
| **Project Base Point** (N/S, E/W, Elev labels; no rotation of stored data) | **Global Origin** (`ModelInfo.GlobalOrigin`) | Both are labels on a fixed frame. GO cannot rotate. GO is what the status bar, `XY=`, AccuDraw, DWG export and Navisworks "Align Global Origins" use. |
| **Survey Point / Shared Coordinates** (site frame: translation + True North rotation) | **Active or named ACS** (origin + rotation + scale) — the only rotated Cartesian frame per model. Georeferenced flavour: **GCS Helmert local transform**. | OBD's IFC exporter takes IfcMapConversion Easting/Northing from the GCS Helmert offsets. |
| **Angle to True North** | `ModelInfo.Azimuth` (DEFINE NORTH), Angle Readout *Direction Mode/Base*, Light Manager "True North … relative to X", ACS rotation, GCS `GetConvergenceAngle` (grid vs true north) | MicroStation users model in grid north; rotation for sheets is done by view rotation / saved views / ACS, never by a model flag. |
| **SiteLocation** lat/long + `GeoCoordinateSystemId` | **GCS** (`DgnGCS.FromModel`): EPSG via `GetEPSGCode`, WKT via `GetWellKnownText`, lat/long of any point via `LatLongFromUors` | MicroStation's GCS is much richer than Revit's (full projection, datum shift, vertical datum, local transform). |
| Link "Auto – Origin to Internal Origin" | Attachment **Coincident** | design plane ↔ design plane |
| Link "By Shared Coordinates" | Attachment **Coincident – World** (aligns GOs) or **Geographic – Reprojected / AEC Transform** (via GCS) | |
| iModel `globalOrigin` / `ecefLocation` / `geographicCoordinateSystem` | DGN GO / `GetLinearTransformToECEF` / GCS | Bentley's own connector mapping (§2.9). |

### The four numbers to extract from a DGN model

1. `GO` — `ModelInfo.GlobalOrigin` (UORs) and `UorPerMaster`, `UorPerMeter`, `UorPerStorage`.
2. `ACS` — active ACS of the active view (`ACSManager.GetActive(vp)`) and named ACSs (`ACSManager.Traverse`): origin (UOR), `DMatrix3d` rotation, scale, type.
3. `GCS` — `DgnGCS.FromModel(model, true)`: name, EPSG, WKT, units, datum, Helmert params, paper scale; `CartesianFromUors`, `LatLongFromUors`, `GetConvergenceAngle`.
4. `Azimuth` — `ModelInfo.Azimuth` (degrees, −180..180), plus reference attachment placement (`DgnAttachment.GetTransformToParent`).

---

## 2. MicroStation coordinate concepts in depth

### 2.1 Design plane, UOR, working units, Solids Working Area

- **Design plane / cube.** V7: an integer space of 2^32 UORs per axis, centre at 2^31. V8+: IEEE doubles ("some two million times larger in each direction" — Bentley KB0108703), but "accuracy degrades the further you are from the centre". Seed files place the GO at the centre and label it 0,0,0. **[web]**
- **UOR** = "unit of resolution, the smallest distance MicroStation can address". All element coordinates are stored in UORs relative to the design-plane centre. **[verified: ODA headers, DgnPlatform docs]**
- **Resolution** = UOR per *storage unit* (Design File Settings ▸ Working Units ▸ Advanced ▸ Resolution). Default 10 000 UOR/m. "Changing the Resolution setting changes the size of existing geometry in the model." **[web: Bentley help]**
- **Storage unit ⊃ master unit ⊃ sub unit.** Master/sub are readout units (MU:SU:PU). `ModelInfo.UorPerMaster`, `UorPerSub`, `UorPerStorage`, `UorPerMeter`. ODA's `OdDgModel::WorkingUnit` enum makes the layering explicit: `kWuUnitOfResolution`, `kWuStorageUnit`, `kWuWorldUnit` (meters), `kWuMasterUnit`, `kWuSubUnit`. **[verified]**
- **Solids Working Area (SWA)** — `ModelInfo.SolidExtent` / `OdDgModel::getSolidExtent()`: the cube (default ≈ 4.29 km per axis around UOR 0) where Parasolid is reliable. "The Solids Working Area does not move with the assignment of a Global Origin as Global Origin defines just the offset from the design file origin and is primarily for display purposes and not calculations." **[web: Bentley help]** → geometry placed far from the design-plane centre (e.g. state-plane coordinates without a GO shift) is imprecise for solids; worth a receive-side warning.
- Compatibility switch: `_USTN_CAPABILITY = -CAPABILITY_LARGE_DESIGN_PLANE` limits V8 to the V7 plane (KB0110177). **[web]**

### 2.2 Global Origin

**Definition.** The GO is *not* a transform applied to stored data. It is the design-plane point that reads 0,0,0. C++ doc for `ModelInfo::SetGlobalOrigin`: **"Sets an offset that is applied to all cartesian point input and output."** **[verified: Python stub of the C++ doc]** ODA: "The global origin defines the start position (zero point) of the world coordinate system inside of the model space." **[verified: DgModel.h:389]**

```
readout_xyz (master units) = (UOR_xyz − GO_uor) / uorPerMaster
```

Every DgnPlatformNET / ODA geometry getter returns design-plane UORs; you must subtract the GO to get what the user sees. The connector and dgnextract already do this (§6).

**Storage.** Part of the model header (`ModelInfo`; ODA `OdDgModel::getGlobalOrigin()`). Per model. Not a graphic element. V7 stored it in the TCB (type 9) at byte offsets 1240–1263 as three VAX doubles. **[web]**

**Defaults.** V8/V8i/CONNECT seeds: GO at the design-plane centre = (0,0,0) UOR. KB0039943 claims XM used the lower-left corner — contradicted by other sources; **[unverified]**. The V8 imperial seed once shipped with `GO=?` reporting 7045.5,7045.5,7045.5 = half the 2^32-UOR V7 cube (KB0108703). WSDOT's V8i seed had a GO shifted −2.1475 on each axis from the design-plane centre and documents that *Geographic – Reprojected* attachment of such 3D files was broken because of it. **[web]**

**UI.** Design File Settings ▸ Working Units ▸ Advanced Settings ▸ Edit (older builds) or the Global Origin dialog via key-in, with modes *Monument Point* ("input of x, y, and z coordinate values … at a selected monument point") and *Center* ("Resets the global origin to its default position at the center of the design plane or cube"). "Setting the global origin … cannot be undone" but it is a saveable setting (File ▸ Save Settings). **[web]**

**Key-ins.** **[web]**

| Key-in | Effect |
|---|---|
| `ACTIVE ORIGIN x,y[,z]` / `GO=x,y[,z]` then data point | assigns those coordinates to the identified monument point |
| `GO=0,0,0` then data point | makes the identified point read 0,0,0 |
| `GO=` , Mode = Center | GO back to the design-plane centre |
| `GO=x,y,z` then **Reset** | assigns the coordinates to the lower-left corner of the imaginary 2^32-UOR V7 cube (KB0108703) |
| `GO=0,0,0;xy=0,0,0\|UOR` | sets 0,0,0 exactly at the design-plane centre (chained key-in) |
| `GO=$` / `ACTIVE ORIGIN $` / `GO=?` | reports the GO. CONNECT help: "the global origins' offset from the design plane center"; KB0108703 (2003): distance from the V7 lower-left corner — the two docs disagree; **[unverified which convention 2026 prints]** |
| `SET TPMODE LOCATE` / `SET TPMODE ACSLOCATE` | tentative/readout relative to GO / active ACS |

**Consequences.**
- Two files whose readout is "correct" but whose GOs differ are *not* aligned in the design plane; only *Coincident-World* aligns them (§2.7).
- DWG has no GO; DGN→DWG bakes the GO offset into coordinates (ODA import applies `translation(-globalOffset)` to all entities and camera positions). **[verified: DgnImportTables.cpp:1797–1839, 3884–3905]**
- Bentley staff recipe to move a file to a new GO/units: attach the old file *Coincident World* with *True Scale*, then merge (KB0026925). **[web]**
- DOT guidance: keep the GO at the design-plane centre in OpenRoads seeds; "sheet tools do not deal well with global origin shifts". **[web]**

### 2.3 Auxiliary Coordinate Systems (ACS)

- "An auxiliary coordinate system (ACS) is a coordinate system with an orientation, and/or an origin, different from those of the DGN file coordinates (the Global system)… called UCS by some other CAD systems." Types **Rectangular** (X,Y,Z), **Cylindrical** (R,θ,Z), **Spherical** (R,θ,φ). Managed enum adds `None=0`, `Extended=4`; ODA 27.5 adds `kGeographic=0`, `kMilitaryGrid=4`, `kMilitaryGridWGS84=5`. **[verified]**
- Attributes: name, description, **origin (UORs, design-plane frame)**, rotation (`DMatrix3d` / `RotMatrix`), **scale**, flags (`ViewIndependent`), read-only flag. **[verified]**
- **Storage.** Named ACSs are control elements in the model: **type 5 (GroupData), subtype 3 (AuxiliaryCoordinateSystem)** (VBA `ScanForACSElements` doc; ODA `OdDgACS : OdDgElement`, persisted with `OdDgAcsXAttribute` type `0x57170001`). The *active* ACS is stored (a) on the model header (`OdDgModel::getAcsType/Origin/Rotation/ElementId`) and (b) since V8i **per view** in `ViewInfo` (`OdDgView::getAcs*`, managed `ViewInformation.GetAuxCoordinateSystem()`), saved with Save Settings. **[verified]**
- **ACS never changes stored geometry.** It is an input (`AX=`, `AD=`, `POINT ACSABSOLUTE`, AccuDraw) and readout (`SET TPMODE ACSLOCATE`, status bar "ACS Position") device. A user asking why readout stays global after activating an ACS is answered by the readout mode. **[web]**
- **Locks.** *ACS Plane Lock* — "forces each data point to lie on the XY-plane of the active ACS"; `ModelInfo.IsAcsLocked` ("user supplied data points will be constrained to lie on the xy plane of the active ACS"); *ACS Plane Snap*; `ACCUDRAW LOCK GRIDPLANE`. **[verified/web]**
- **UI.** *Auxiliary Coordinates* dialog (`DIALOG COORDSYS`, Ctrl+F9): Name, Scale, Origin X/Y/Z, Orientation, Rotation, Type, Description; tools Define ACS by Element / by Points / by View / by Reference, Rotate ACS, Move ACS, Select ACS; context menu Set Active View, Set all Views, Update From Active, Reset To Global, Toggle View Independent, Copy, Delete, Rename; import from another DGN. Triad via View Attributes / `SET ACSDISPLAY ON|OFF`. Legacy key-ins `ACS DEFINE|ATTACH|SELECT|ROTATE|SCALE|DELETE|SAVE`, `RX=`, `PX=` **[unverified spelling for CONNECT]**. Config `MS_SETDEFAULTVIEWACSFROMMODEL`. **[web]**
- **GCS shows up as an ACS.** "When a geographic coordinate system is assigned to a model, it becomes available in the Auxiliary Coordinates dialog and can be activated as the active ACS" (lat/long input via `POINT ACSABSOLUTE` with DMS formats). These auto-created ACSs are read-only and are deleted via the GCS dialog. **[web]**
- Evolve "Creating a Project ACS": ACS is used to "rotate the views to suit your project" and for "coordinate readout based on a local, site coordinate system" — i.e. ACS is MicroStation's *Project-North-like* device, but only for view/readout. **[web]**

**World → ACS math** (VBA sample `ussmpACSTransform`, same for .NET): `toACS = Rotation · Translate(−Origin)`, `fromACS = inverse`. Whether `GetRotation` rows are the ACS axes in world coordinates (world→ACS) or columns (ACS→world) must be confirmed live. **[unverified]**

### 2.4 Geographic Coordinate System (GCS)

**Purpose.** "Geo-Coordination lets you specify or view the position of your design content on the earth's surface"; enables lat/long readout and entry, referencing other geolocated designs and rasters, reprojection, Bing/Google Earth, GPS. "For infrastructure designs occupying a volume of less than a cubic kilometer, the curvature of the earth does not need to be considered." **[web: Bentley help]**

**Model.** A GCS = CS-Map projected or geographic CRS + datum + ellipsoid + vertical datum + optional **local transform**. The GCS maps the model's design coordinates (UOR, GO applied — see `CartesianFromUors`: "Calculates the cartesian coordinates in the units specified by the GCS from design coordinates (UORs)") onto the CRS. In the common case model X/Y in master units **are** Easting/Northing. **[verified: C++ doc via stub]**

**Storage.** A non-graphic control element owned by the model: ODA `OdDgGeoDataInfo` (signature `0x00B6`), returned by `OdDgModel::getGeoData()`; managed `DgnGCS.FromModel` "attempting to locate the element that saves the geographic coordinate system parameters in the model". Two slots: *primary* and *"reference"* (`primaryCoordSys: bool`; VBA help: "Always pass True"). Assigning a GCS also creates read-only ACSs. Not storable: custom datum grid-file paths > 75 chars, > 1 chained geodetic transform. Optional **KML placemark** elements (`kKMLPlacemarkElement = 0x006E0000`) tie a model XYZ to lat/long. `OdDgGeoDataInfo` also records `getNumberOfSubUnitsInMasterUnit()` / `getNumberOfUORSInSubUnit()` — the working-unit ratios at assignment time. **[verified]**

**UI.** Utilities ▸ Geographic ▸ Coordinate System (V8i: Tools ▸ Geographic ▸ Select GCS): Details, **From Library**, **From Placemarks**, **From Reference**, **To Reference**, **From File**, Edit Reprojection Settings, Delete GCS. Library tab: Geographic (lat/long) vs Projected by region; Search tab. **[web]**

**Key-ins.** `GEOCOORDINATE ASSIGN <GCSName>` with `NOQUERY` (assign silently), `REPROJECT` (reproject data), `MATCHUNITS` ("corrects GCS and matches storage units without reprojection"); `EXPORT GOOGLEEARTH [<file>]`; `GOOGLEEARTH PLACEMARK DELETE`. **[web]**

**Correct vs Reproject.** When a GCS already exists: "Correcting the Geographic Coordinate System – do not reproject the data" (re-label) vs "Reproject the data to the new Geographic Coordinate System" (permanently moves data; undoable). Changing linear units also asks about *Storage Units*. **[web]**

**Reprojection pipeline** (KB0026699): model units → GCS Cartesian units → lon/lat/elev in source datum → datum shift → target Cartesian → model units. Settings: stroke tolerance; reproject cells/text individually (Always/Never/If Spatially Large > 0.2 km²); rotate/scale cells and text by convergence angle / grid scale; stroke arcs/ellipses/curves; add points; separate settings for references vs active model. **[web]**

**Local transform (Helmert).** GCS Properties ▸ *Local Transform Type* = None / **Helmert** / Second-order conformal, with Helmert A, Helmert B, Offset X, Offset Y[, Z]. Bentley help: "x' = a·x − b·y + c; y' = b·x + a·y + d; z' = z + e … a = s cos r, b = s sin r … x', y', z' are the easting, northing, and elevation in the GCS". Uses: grid-to-ground (VDOT: Helmert A = 1/scale factor; OHDOT: LCC "with Affine Processor" in `.dty` user libraries via `MS_GEOCOORDINATE_USERLIBRARIES`), legacy shifted coordinates, and IfcMapConversion Easting/Northing (IFC-SG OBD guide). Files sharing a base GCS that differ only by local transform get "an exact linear transform" instead of reprojection. **This is the closest analogue of Revit shared coordinates on top of a projected CRS.** **[web + verified ODA struct]**

**Paper scale.** `DgnGCS.PaperScale` — "affects the Cartesian coordinates and makes measurements unreliable… default and recommended value is 1.0" (legacy). **[verified]**

**Placemark monuments / structure-scale GCS** (OpenBuildings workflow, KB0109867/KB0099480): a placemark is a `KmlPlacemark` cell (SystemCells.dgnlib) with Name/Longitude/Latitude/Altitude; ≥ 2 placemarks → *From Placemarks* computes an **Azimuthal Equal Area** GCS centred on the primary placemark (usable ≈ 2 km, distortion limits 0.01 % / 2'). This is the "building near 0,0 plus two monuments" case a Speckle connector will meet. **[web]**

**Vertical.** `BaseGCS.VerticalDatumName`, `VerticalDatumUserInterfaceCode {FromDatum=100, NGVD29, NAVD88, Geoid, Ellipsoid, LocalEllipsoid}`, `GeoidSeparation`, `ReprojectElevation`; ODA `kMatchDatum / kGeoid`. **[verified]**

**Known defect.** `BaseGCS.LatLongFromCartesian` off by ~100 m for British National Grid (OSTN grid-shift path), Bentley-confirmed CE U11–U16, status in 2026 unknown. Prefer `DgnGCS.LatLongFromUors`. **[web]**

**IFC.** Bentley (accepted answer): "When we export IFC from a DGN containing a transformed GCS the latitude and longitude are stored in the IFC … At present MicroStation, and hence OpenBuildings, does not read this information from incoming IFC files." **[web]**

### 2.5 North: DEFINE NORTH / azimuth, angle readout, solar, grid convergence

- **`DEFINE NORTH`** key-in: "redefines true North from its default position (the design plane y-axis) to any direction in the design plane for precision input key-ins and computing direction measurements" and "is utilized to calculate the direction of the Sun for Solar Lighting". API `ModelInfo.Azimuth` (get/set, "ERROR if < −180.0 or > 180.0"). The DEFINE NORTH ↔ `Azimuth` mapping is inferred, not documented. **[web + verified signature; mapping unverified]** ODA has `OdDgDatabase::getAzimuth()` ("azimuth true north angle") on the file header, not on the model. **[verified]**
- **Angle Readout** (Design File Settings): Format, Accuracy, **Direction Mode** *Azimuth* ("clockwise from the design plane positive y-axis; used in surveying") or *Bearing* (N 45°E), **Base** (North/South/East/West/Custom), **Clockwise**. API `ModelInfo.DirectionMode`, `DirectionBaseDir`, `DirectionClockwise`, `AngularMode`; ODA `getAngleDirectionMode {kAzimut, kBearing}`, `getAngleReadoutDirectionBase {kCustom, kEast, kNorth, kWest, kSouth}`, `getAngleReadoutDirection()`. **[verified]**
- **Solar / Light Manager**: Solar type Time & Location (lat/long/city/KML) or Direction (Azimuth 0–360, Altitude); a separate **"True North … direction of true north relative to the X axis"** field (note: different convention from DEFINE NORTH's +Y default). Managed `SolarLight { SolarType, AzimuthAngle, AltitudeAngle, GeoLocation (GeoPoint2d), GmtOffset, Year… }`, `SolarUtility.GetSolarDirection(...)`, `DirectionToAzimuthAngles(...)`; ODA `OdDgDatabase::getLatitude/getLongitude` (DMS ints), `getSolarDirection()`. If no GCS/placemarks exist, KML export uses these solar lat/long values for the model origin. **[verified + web]**
- **Grid north vs true north** at a location: `BaseGCS.GetConvergenceAngle(ref GeoPoint)` (degrees) and `GetGridScale(ref GeoPoint)`. **[verified]**

### 2.6 Grid, views, view rotation

- **Grid** is a UI aid: `ModelInfo.GridBase (DPoint2d)`, `GridAngle`, `GridRatio`, `GridPerReference`, `UorPerGrid`, `IsoGrid`, `IsGridLocked`, `SetGridParameters(...)`; orientation View/Top/Right/Front/**ACS** (ODA `OdDgGridOrientationType`). The grid "passes through the global origin". No separate "grid origin" datum exists. **[verified]**
- **Views**: 8 per view group (`OdDgView`, managed `ViewInformation { Origin, Rotation, Delta, ViewFlags }`), orthographic or camera; camera position is in the design-plane frame (ODA subtracts the GO when exporting cameras). View rotation (`ROTATE VIEW`, standard views) is purely a view property; *Define ACS by View* turns it into an ACS. **[verified + web]**

### 2.7 Reference attachments and their transform

**Stored parameters** (managed `DgnAttachment`, ODA `OdDgReferenceAttachmentHeader`, COM `Attachment`): **[verified]**

- **Master Origin** — "the location in the parent model at which the referenced model should be displayed. Specified in parent model coordinates" (UORs of the parent).
- **Reference Origin** — "the location in the referenced model that should coincide with the master origin… Defaults to 0,0,0. Specified in referenced file UORs".
- **Rotation matrix** (ODA: "this matrix is for rotation only").
- **Stored scale / display scale / scale mode** `TrueScale (default) | StorageUnits | Direct`: "If the scale mode is Direct the displayed scale is the same as the display scale — otherwise the displayed scale is calculated at load time based on the stored scale, the ScaleMode, and the units of the DgnAttachment model and the parent model." ODA `getScale()` vs `getEntireScale()` ("ratio: working units of a referenced model / working units of the current model").
- Nest depth, display flag, clip points/element, front/back clip, camera on, "true scale", "scale by storage units", "do not display as nested", missing-file/inactive flags.

**Net transform** — `DgnAttachment.GetTransformToParent(out DTransform3d, scaleZfor2dRef)`: "composed of unit scaling, the offset from the origin in the referenced model to the master origin, any specified scale factor, and any specified rotation matrix". The ODA sources make the order explicit (identical in 5 places): **[verified]**

```
p_parent = T(masterOrigin) · R · S(entireScale) · T(−(refOrigin [+ insertionBase]) · uor→wu(refModel)) · p_ref
```

**Orientation options** (Reference Attachment Properties): **[web]**

| Option | Meaning |
|---|---|
| **Coincident** | design-plane coordinates aligned; no rotation/scale/offset; **GO ignored** |
| **Coincident – World** | "Aligns the reference with the active model with regard to both Global Origin and design plane coordinates … available only when referencing a model in a DGN file". In the stored data it is just a master/ref origin pair compensating the GO difference (ODA `getCoincidentFlag()`; no separate "world" flag in the managed API **[unverified]**). "If two files use the same Global Origin, Coincident and Coincident World are identical. Coincident World is also the method used when working with DWG references" (KB0108703). |
| Standard views / Saved views / Named boundaries | rotation from a view |
| **Geographic – Reprojected** | per-point reprojection via both models' GCSs; "slower, more accurate … for larger geographic areas"; temporary, not written back; cannot be moved/scaled/activated |
| **Geographic – AEC Transform** | linear best-fit from `DgnGCS.GetLocalTransform`; "quicker … for geographically small areas" (< ~1 km²) |

Config: `MS_REF_DEFAULTSETTINGS = attachMethod=coincident|world|geoReprojected|geoAECTransform, trueScale=0|1, …`, `MS_REF_COINCIDENTWORLD=1`, `MS_REF_MAXNESTDEPTH`. Key-ins `REFERENCE ATTACH`, `RF=[cfgvar:]<file>[,model][,logical]…`, `REFERENCE MOVE|ROTATE|SCALE …`. **[web]**

Managed `GeoCoordinationState {NoGeoCoordination=0, Reprojected=1, AECTransform=2}` exists only as an event-handler enum; `DgnAttachment.IsMissingGeoCoordSys` = "set to be Geographically attached, but there is no GCS associated with either the attachment model or the parent model". **Open question:** what `GetTransformToParent` returns for geographic attachments (AEC linear approximation, identity, or the coincident placement). **[unverified]**

### 2.8 OpenRoads / OpenRail / OpenBridge / OpenBuildings specifics

- **ORD adds no new coordinate model.** Civil geometry is stored in design-plane coordinates with X = Easting, Y = Northing under the model's GCS. ORD 2025 ships the same `Bentley.DgnPlatformNET` / `Bentley.DgnGeoCoord2` surface as MicroStation 2026 (public members of `ModelInfo`, `ACSManager`, `AuxiliaryCoordinateSystem`, `DgnGCS` diffed by reflection: identical). `Bentley.CifNET.*` (`OpenRoadsDesigner\Cif\`) handles alignments/corridors; the GCS is MicroStation's. **[verified]**
- Bentley/OHDOT: "When a GCS is initially selected, you are simply defining the coordinate system where the data resides. Choosing a GCS when one has not been previously defined does not re-project existing data… It is not intended to be used 'on the fly' to translate the data from grid to ground"; "The Geographic Coordinate System must be assigned to each of the Seed files in the WorkSet". Bing Maps, survey/terrain import, GIS import and reality data all require the model GCS. **[web]**
- **Civil readout**: Design File Settings ▸ Civil Formatting ▸ Coordinate Settings "X, Y" vs "Northing, Easting"; bearing/azimuth follows *Angle Readout* Direction Mode. Model Annotation Scale is unrelated to coordinates. **[web]**
- **OpenBridge Modeler**: seed per GCS/zone; references attached Coincident World. **OpenBuildings Designer**: structure-scale placemark workflow (§2.4), Master/Project GCS file method, IfcSite from the GCS; AECOsim also had a product-specific "Global Offset Coordinates" (RAM exchange), unrelated to the MicroStation GO. **[web]**
- GO guidance for ORD: keep GO at the design-plane centre; sheet tools assume `GO=0,0`; historic shifted-GO DOT seeds caused SS4/ORD issues; Coincident-World reconciles mismatched GOs. **[web]**

### 2.9 Bentley's own mapping: DGN → iModel

iTwin.js defines three things per iModel, and the Bentley DGN connector maps the DGN concepts onto them: **[web: itwinjs.org]**

| iModel | Definition | DGN source |
|---|---|---|
| `IModel.globalOrigin` | "An offset to be applied to all spatial coordinates… normally used to transform spatial coordinates into the Cartesian coordinate system of a Geographic Coordinate System"; "the Connector must subtract that global origin from the source data" | DGN Global Origin (in meters) |
| `IModel.ecefLocation` | `{ origin, orientation (YawPitchRoll), cartographicOrigin?, xVector?, yVector? }` — the global origin's position and orientation in ECEF, a **linear** transform about one point ("buildings and plants… Open Building Designer, OpenPlant, and Revit") | `DgnGCS.GetLinearTransformToECEF(extent)` (C++ 2024 SDK) |
| `IModel.geographicCoordinateSystem` | for **Projected** geolocation ("Bentley Map, OpenRoads, Civil 3D, GIS") | DGN GCS |

"For historical reasons, some applications such as MicroStation store a GCS, even for a Linear GeoLocation." All iModel coordinates are meters; the first file with a CRS is the spatial root. This triple (offset + linear ECEF placement + CRS) is exactly what a Speckle `modelPlacement` / `geolocation` / `crs` record should carry.

---

## 3. How the Revit connector implements reference points (the pattern to copy)

All paths relative to the repo root; line numbers from the current tree.

### 3.1 Design in one paragraph

A per-model-card **send/receive setting** (`ICardSetting`, `Type="string"`, `Enum=[InternalOrigin, ProjectBase, Survey, SharedCoordinates]`, id `"referencePoint"`) is resolved into one Revit `DB.Transform` stored as **datum → internal** (the datum's placement expressed in internal coordinates). It lives in the immutable `RevitConversionSettings` record inside a scoped `IConverterSettingsStore<T>` stack. Every low-level `XYZ → Point/Vector` converter calls `IReferencePointConverter.ConvertToExternalCoordinates` (applies `transform.Inverse`) **before** unit scaling on send; every `Point → XYZ` converter calls `ConvertToInternalCoordinates` (applies `transform`) **after** scaling on receive. Instances get the transform composed onto the **outermost** placement only; definition geometry is converted with the transform suppressed via `settingsStore.Push(s => s with { ReferencePointTransform = null })`. A boolean **Apply Transform** setting decides whether the transform is baked into geometry; the 4.0 bundle records the datum, all available datum options, base-point positions, true-north angle, site lat/long and CRS as `modelPlacement.*`, `referencePoints.*`, `projectLocation.*`, `geolocation.*`, `crs.*` model properties.

### 3.2 Files

| Piece | File |
|---|---|
| Enums | `Converters/Revit/Speckle.Converters.RevitShared/Settings/ReferencePointType.cs` (`InternalOrigin, ProjectBase, Survey, SharedCoordinates`), `ReceiveReferencePointType.cs` (adds `Source`) |
| Card settings | `Connectors/Revit/Speckle.Connectors.RevitShared/Operations/Send/Settings/SendReferencePointSetting.cs`, `SendApplyTransformSetting.cs`, `Operations/Receive/ReceiveReferencePointSetting.cs` |
| Surfacing | `Bindings/RevitSendBinding.cs:148-158` (`GetSendSettings`), `RevitReceiveBinding.cs:28-29` |
| Resolution + cache | `Operations/Send/Settings/ToSpeckleSettingsManager.cs:40-100, 244-286`; receive `Operations/Receive/ToHostSettingsManager.cs:72-115` |
| Settings record | `Converters/Revit/Speckle.Converters.RevitShared/Settings/RevitConversionSettings.cs`, factory `RevitConversionSettingsFactory.cs:14-43` |
| Converter | `IReferencePointConverter.cs`, `ReferencePointConverter.cs` |
| Application | `ToSpeckle/Raw/Geometry/XyzConversionToPoint.cs:25-37`, `VectorToSpeckleConverter.cs`, `Helpers/DisplayValueExtractor.cs:223-240, 300-368`; receive `ToHost/Raw/Geometry/PointConverterToHost.cs`, `MeshConverterToHost.cs:123-148`, `RevitHostObjectArtefactBuilder.cs:1568-1639` |
| Metadata | `Operations/Send/RevitArtifactRootObjectBuilder.cs:510-651`, `Helpers/ReferencePointHelper.cs` |
| Shared infra | `Sdk/Speckle.Converters.Common/ConverterSettingsStore.cs`, `DUI3/Speckle.Connectors.DUI/Settings/CardSetting.cs`, `Sdk/Speckle.Connectors.Common/Operations/RootKeys.cs:6` (`REFERENCE_POINT_TRANSFORM = "referencePointTransform"`) |

### 3.3 Key code

Setting declaration (dropdown = `Type="string"` + `Enum`; toggle = `Type="boolean"`):

```csharp
[SuppressMessage("Usage", "CA2263:Prefer generic overload when type is known", Justification = "Multi-targeting friction")]
public class SendReferencePointSetting(ReferencePointType value = SendReferencePointSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "referencePoint";
  public const ReferencePointType DEFAULT_VALUE = ReferencePointType.InternalOrigin;
  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Reference Point";
  public string? Description { get; set; } = "Selects the model placement datum. ...";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(ReferencePointType)).ToList();
  public object? Value { get; set; } = value.ToString();
  public static readonly Dictionary<string, ReferencePointType> ReferencePointMap = ...;
}
```

Transform resolution (`ToSpeckleSettingsManager.GetTransform`): `ProjectBase` / `Survey` → `Transform.CreateTranslation(basePoint.Position)` (translation only); `SharedCoordinates` → `document.ActiveProjectLocation.GetTotalTransform()` (translation + true-north rotation); `InternalOrigin` → `null`. The per-card cache compares the previous and current transform and evicts the send conversion cache on change (`sendConversionCache.EvictObjects(unpackedIds)`), because base points can move between sends.

Settings record (the quartet worth copying verbatim):

```csharp
public record RevitConversionSettings(
  DB.Document Document, DetailLevelType DetailLevel,
  DB.Transform? ReferencePointTransform,          // null unless ApplyTransform — converters use only this
  bool ApplyTransform, string SpeckleUnits, ...,
  ReferencePointType ReferencePointKind = ReferencePointType.InternalOrigin,   // for metadata
  DB.Transform? ModelReferencePointTransform = null);                          // always retained for metadata
```

Factory: `applyTransform ? referencePointTransform : null` for the first, `referencePointTransform` always for the last.

Converter:

```csharp
public DB.XYZ ConvertToExternalCoordinates(DB.XYZ p, bool isPoint) =>
  converterSettings.Current.ReferencePointTransform is DB.Transform t
    ? (isPoint ? t.Inverse.OfPoint(p) : t.Inverse.OfVector(p)) : p;
public DB.XYZ ConvertToInternalCoordinates(DB.XYZ p, bool isPoint) =>
  converterSettings.Current.ReferencePointTransform is DB.Transform t
    ? (isPoint ? t.OfPoint(p) : t.OfVector(p)) : p;
```

Send order: `extPt = ConvertToExternalCoordinates(xyz, true)` **then** `ScaleLength(...)`. Receive order: scale **then** `ConvertToInternalCoordinates`. Instances: `localToWorld = documentToWorld.Multiply(localToDocument)` goes into `InstanceProxy.transform` (translation scaled to Speckle units, rotation untouched); definition meshes untouched; curves get only the instance transform since the point converters already apply the datum (double-transform bug #1231). Linked documents: `transform = (mainModelTransform ?? Identity).Multiply(linkedModel.GetTotalTransform().Inverse)` pushed per document.

Receive composition: `effective = rootTransform.Multiply(receiveTransform)` (`ReferencePointHelper.CalculateNewTransform`); the bundle's `modelPlacement.transform` (INTERNAL → datum) is inverted on read; `appliedToGeometry` decides unbaking; receive has no Apply Transform setting since #1540.

### 3.4 Metadata rows (4.0 bundle, `eav.model`)

| Key | Value |
|---|---|
| `modelPlacement.options.{internalOrigin\|projectBasePoint\|surveyPoint\|sharedCoordinates}.transform` | 16-value **row-major** CSV, INTERNAL → datum, translation in Speckle units, `"R"` formatting |
| `modelPlacement.default` / `.transform` / `.units` / `.source` / `.appliedToGeometry` | effective datum kind (fallback → `internalOrigin`), its transform, units, requested kind incl. `internalOriginFallback`, bake flag |
| `referencePoints.projectBasePoint.position.{x,y,z}`, `referencePoints.surveyPoint.position.*`, `.sharedPosition.*` | scaled positions |
| `projectLocation.trueNorthAngle` | `ProjectPosition.Angle`, units `"rad"` |
| `geolocation.anchor.latitude/longitude` (`"deg"`), `.elevation`, `.source="siteLocation"`, `.referencePoint`, `.coordinateSpace` | from `SiteLocation` |
| `crs.horizontal.authority/code/nativeCode/definition`, `crs.units` | from `SiteLocation.GeoCoordinateSystemId/Definition` |

Contract (commit `0313a8987`): `modelPlacement.transform` is **always** INTERNAL → datum; `appliedToGeometry` says whether stored coordinates already include it. v1 root object path writes `root["referencePointTransform"] = { "transform": double[16] column-major, internal feet }` only when baked. AutoCAD/Civil3D use the same rows with `AutocadModelPlacement { DrawingWcs, CurrentUcs, GridCoordinates }` (UCS = the AutoCAD analogue of ACS) plus `coordinateOperation.localToGrid.*`. There is **no shared** `ReferencePointSetting` class; only `ICardSetting`, `ModelCard.Settings`, `IConverterSettingsStore<T>` and `RootKeys` are shared.

---

## 4. Bentley managed C# API — read and write, per concept

Environment **[verified by reflection]**: MicroStation 2026 (26.00.01.520) at `C:\Program Files\Bentley\MicroStation 2026\MicroStation\`; all managed assemblies target .NET Framework 4.8 (CONNECT U13–U17 was 4.6.2). No `*.xml` IntelliSense files or C++ headers are installed; the C++ doc comments are available via the Python stubs at `C:\ProgramData\Bentley\PowerPlatformPython\Examples\Microstation\Intellisense\MSPyDgnPlatform.pyi`; the COM object model is documented in `MicroStation\MicroStationVBA.chm`.

| Assembly | Contents |
|---|---|
| `Bentley.DgnPlatformNET.dll` | `ModelInfo`, `DgnModel(Ref)`, `DgnAttachment`, `ACSManager`, `AuxiliaryCoordinateSystem`, `ViewInformation`, `SolarLight`, `UnitDefinition` |
| `Bentley.DgnGeoCoord2.dll` | **`Bentley.GeoCoordinatesNET.DgnGCS`** — **not referenced by our csprojs yet** |
| `Bentley.GeoCoord2.dll` | `Bentley.GeoCoordinatesNET.BaseGCS`, `Datum`, `Ellipsoid`, `Unit`, `LocalTransformParams`, `LibrarySearcher`, `GeoCoordinateManager` — **not referenced yet** |
| `Bentley.GeometryNET.Structs.dll` | `DPoint3d`, `DVector3d`, `DMatrix3d`, `DTransform3d`, `GeoPoint`, `GeoPoint2d` |
| `ustation.dll` (`Bentley.MstnPlatformNET`) | `Session`, `Settings`, `AddIn.AcsOperationEventHandler` |
| `Assemblies\Bentley.Interop.MicroStationDGN.dll` | COM/VBA object model |

### 4.1 Global Origin and working units — `ModelInfo`

```csharp
public class ModelInfo : IDisposable {
  DPoint3d GlobalOrigin { get; set; }             // UORs, design-plane frame
  double UorPerMaster { get; }  double UorPerSub { get; }  double UorPerStorage { get; }  double UorPerMeter { get; }
  UnitDefinition GetMasterUnit(); UnitDefinition GetSubUnit(); UnitDefinition GetStorageUnit();
  void SetWorkingUnits(UnitDefinition master, UnitDefinition sub);      // "Master unit must be larger than sub unit"
  void SetStorageUnit(UnitDefinition storage, double uorPerStorage);    // "Change the resolution settings"
  void SetUorPerStorageUnit(double uorPerStorage);
  double Azimuth { get; set; }                     // "Sets rotation Azimuth. ERROR if <-180.0 or >180.0"
  double DirectionBaseDir { get; set; } bool DirectionClockwise { get; set; }
  bool Is3d { get; set; } bool IsTreatedAs3d { get; } bool IsAcsLocked { get; set; } bool IsGridLocked { get; set; }
  DPoint3d InsertionBase { get; set; }  double SolidExtent { get; set; }
  double GridAngle { get; } DPoint2d GridBase { get; } uint GridPerReference { get; } double GridRatio { get; } double UorPerGrid { get; } bool IsoGrid { get; set; }
  void SetGridParameters(double uorPerGrid, uint gridPerReference, double gridRatio, DPoint2d gridBase, double gridAngle);
  string Name { get; set; } string Description { get; set; } DgnModelType ModelType { get; set; }
  event ModelInfoChangedEventHandler ModelInfoChanged;
}
// DgnModelRef: ModelInfo GetModelInfo(); bool Is3d; DgnFile GetDgnFile(); DgnModel GetDgnModel(); DgnModelRef GetParentModelRef();
// DgnModel:    DgnModelStatus SetModelInfo(ModelInfo info); StatusInt SaveModelSettings(); StatusInt GetRange(out DRange3d);
// DgnFile:     StatusInt ProcessChanges(DgnSaveReason reason);
// UnitDefinition: UnitBase Base {Meter=1, Degree=2}; UnitSystem {English=1, Metric=2, USSurvey=3}; double Numerator, Denominator; string Label; StandardUnit IsStandardUnit;
// DgnModelStatus: Success=0, ReadOnly=69633, Mismatch2d3d=69651, MuNotLargerThanSu=69664, NotSameUnitBase=69665
```

There is no `IsUorsPerMeter` member. C++ doc: "To modify the modelinfo you must create a copy of the returned one (`MakeCopy()`), modify it, and call SetModelInfo()"; the managed wrapper has no `MakeCopy`, so treat the returned object as the copy **[unverified]**.

Read:

```csharp
DPN.DgnModel model = Session.Instance.GetActiveDgnModel();
DPN.ModelInfo info = model.GetModelInfo();
BG.DPoint3d goUor = info.GlobalOrigin;
double uorPerMaster = info.UorPerMaster, uorPerMeter = info.UorPerMeter;
// readout of a UOR point p: (p - goUor) / uorPerMaster
// "GO=$"-style offset of the GO from the design-plane centre, master units: goUor / uorPerMaster (sign to confirm live)
```

Write:

```csharp
DPN.ModelInfo info = model.GetModelInfo();
info.GlobalOrigin = new BG.DPoint3d(x * info.UorPerMaster, y * info.UorPerMaster, z * info.UorPerMaster);
DPN.DgnModelStatus st = model.SetModelInfo(info);       // Success == 0
model.SaveModelSettings();
// alternative: Session.Instance.Keyin("GO=0,0,0;xy=0,0,0|uor");  Session.Instance.EnqueueKeyin("ACTIVE ORIGIN ...")
```

`Bentley.MstnPlatformNET.Session`: `static Session Instance`, `DgnModel GetActiveDgnModel()`, `DgnModelRef GetActiveDgnModelRef()`, `DgnFile GetActiveDgnFile()`, `static Viewport GetActiveViewport()`, `void Keyin(string)`, `void EnqueueKeyin(string)`, `bool IsActiveModelReadOnly()`.

**COM interop** (`Bentley.Interop.MicroStationDGN`): `ModelReference.GlobalOrigin` (read-only `Point3d`; units undocumented — VBA is generally master units, **[unverified]**), `UORsPerMasterUnit`, `UORsPerSubUnit`, `UORsPerStorageUnit {get;set;}`, `MasterUnit/SubUnit/StorageUnit`, `Is3D`, `IsAttachment`, `Attachments`, `Range(bool includeAttachments)`, `GetGCS(bool primary)`, `WriteGCS(gcs, primary, reprojectData)`, `DeleteGCS(primary)`, `ReloadGeoReferences()`; `Application.ActiveModelReference`, `ActiveDesignFile.DefaultModelReference`, `ACSManager`, `CreateGCSFromKeyName(string)`; `View.DisplaysAcsTriad`. **No COM setter for GlobalOrigin.**

### 4.2 ACS — `ACSManager` / `AuxiliaryCoordinateSystem`

```csharp
public class ACSManager {
  static AuxiliaryCoordinateSystem CreateACS();
  static AuxiliaryCoordinateSystem GetActive(Viewport vp);
  static StatusInt SetActive(AuxiliaryCoordinateSystem acs, Viewport vp);
  static AuxiliaryCoordinateSystem GetByName(string name, DgnModelRef modelRef, uint options);
  static StatusInt Save(AuxiliaryCoordinateSystem acs, DgnModelRef modelRef, ACSSaveOptions saveOption, ACSEventType eventType);
  static StatusInt Delete(string name, DgnModelRef modelRef);
  static bool Traverse(ACSTraversalHandler handler, DgnModelRef modelRef);
}
public abstract class ACSTraversalHandler {
  abstract int GetACSTraversalOptions();
  abstract bool HandleACSTraversal(string name, string description, ACSType acsType, ACSFlags flags);
}
public class AuxiliaryCoordinateSystem : IDisposable {
  ACSType Type { get; set; } string TypeName { get; } double Scale { get; set; } bool IsReadOnly { get; }
  string GetName(); StatusInt SetName(string); string GetDescription(); StatusInt SetDescription(string);
  DPoint3d GetOrigin(out DPoint3d origin); StatusInt SetOrigin(DPoint3d origin);      // UORs
  DMatrix3d GetRotation(out DMatrix3d rot); StatusInt SetRotation(DMatrix3d rot);
  ACSFlags GetFlags(); StatusInt SetFlags(ACSFlags);
  StatusInt SaveToFile(DgnModelRef modelRef, ACSSaveOptions option); StatusInt DeleteFromFile(DgnModelRef modelRef);
  AuxiliaryCoordinateSystem Clone(); bool Equals(AuxiliaryCoordinateSystem other);
  PointFromStringResult PointFromString(string pattern, bool relative, DgnModelRef modelRef);
  StringFromPointResult StringFromPoint(DPoint3d, DgnModelRef, DistanceFormatter, DirectionFormatter);
}
enum ACSType { None=0, Rectangular=1, Cylindrical=2, Spherical=3, Extended=4 }
enum ACSFlags { None=0, Default=0, ViewIndependent=1 }
enum ACSSaveOptions { OverwriteByElementId=0, OverwriteByName=1, AllowNew=2 }
enum ACSEventType { None=0, ParameterChanged=1, GeometryChanged=2, ChangeWritten=4, NewACS=8, Delete=16 }
// ViewInformation: AuxiliaryCoordinateSystem GetAuxCoordinateSystem(); void SetAuxCoordinateSystem(acs); DPoint3d Origin; DMatrix3d Rotation; ViewFlags.AuxDisplay
// Viewport: ViewInformation GetViewInformation(); DgnModel GetRootModel(); DMatrix3d GetRotation(); DPoint3d GetViewOrigin();
// ustation.dll: AddIn.AcsOperationEventHandler(AddIn, AcsOperationEventArgs { Name, Description, Type, OpType, EventType })
```

Read all named ACSs and the active one:

```csharp
sealed class Collect : DPN.ACSTraversalHandler {
  public List<(string name, string desc, DPN.ACSType type, DPN.ACSFlags flags)> Items = new();
  public override int GetACSTraversalOptions() => 0;             // option bits unverified
  public override bool HandleACSTraversal(string name, string description, DPN.ACSType acsType, DPN.ACSFlags flags)
  { Items.Add((name, description, acsType, flags)); return true; }
}
var h = new Collect(); DPN.ACSManager.Traverse(h, model);
foreach (var it in h.Items) {
  using DPN.AuxiliaryCoordinateSystem acs = DPN.ACSManager.GetByName(it.name, model, 0);
  acs.GetOrigin(out BG.DPoint3d oUor); acs.GetRotation(out BG.DMatrix3d rot); double s = acs.Scale;
}
DPN.Viewport vp = Session.GetActiveViewport();
using DPN.AuxiliaryCoordinateSystem active = DPN.ACSManager.GetActive(vp);     // may be unnamed / Type None
```

Write (forum-verified sequence, C++ CE 10.15, same in .NET):

```csharp
using DPN.AuxiliaryCoordinateSystem acs = DPN.ACSManager.CreateACS();
acs.SetName("Speckle"); acs.SetDescription("Speckle reference point");
acs.Type = DPN.ACSType.Rectangular; acs.Scale = 1.0; acs.SetFlags(DPN.ACSFlags.Default);
acs.SetOrigin(originUor); acs.SetRotation(rotation);
DPN.StatusInt st = acs.SaveToFile(model, DPN.ACSSaveOptions.OverwriteByName);   // persist as element
DPN.ACSManager.SetActive(acs, Session.GetActiveViewport());                      // per-view; Save Settings persists
```

COM: `Application.ACSManager { ACSType, Name, Description, IsDefined, Origin, Rotation, DefineACS(ref Point3d, ref Matrix3d, MsdACSType), SaveActive(name, description, overwrite), AttachNamed(name, …), DeleteNamed(name), ScanForACSElements() }`; `AuxiliaryCoordinateSystemElement : Element { ACSType, Name, Description, Origin, Rotation, Rewrite() }`. The COM `ACSManager` is the *model-level* active ACS (pre-V8i style); `Origin`/`Rotation` raise if none is defined.

### 4.3 GCS — `DgnGCS` / `BaseGCS` (`Bentley.GeoCoordinatesNET`)

```csharp
public class DgnGCS : BaseGCS, IDisposable {                                   // Bentley.DgnGeoCoord2.dll
  DgnGCS(string keyString, DgnModelRef modelRef);      // CS-Map key, e.g. "EPSG:27700", "OSGB-GPS-2015"
  DgnGCS(BaseGCS gcs, DgnModelRef modelRef);
  static DgnGCS FromModel(DgnModelRef modelRef, bool primaryCoordSys);          // null or !Valid if none — guard both
  string DisplayName { get; } string ProjectionName { get; } double PaperScale { get; }
  int SetPaperScale(double value, DgnModelRef modelRef);
  int ToModel(DgnModelRef modelRef, bool primaryCoordSys, bool writeToFile, bool reprojectData, bool showProblems);  // 0 = success
  void CartesianFromUors(out DPoint3d outCartesian, DPoint3d inUors);   void UorsFromCartesian(out DPoint3d outUors, DPoint3d inCartesian);
  int LatLongFromUors(out GeoPoint outLatLong, DPoint3d inUors);        int UorsFromLatLong(out DPoint3d outUors, GeoPoint inLatLong);
  int LatLongFromUors2D(out GeoPoint2d, DPoint2d);  int UorsFromLatLong2D(out DPoint2d, GeoPoint2d);
  int ReprojectUors(out DPoint3d[] outUors, DPoint3d[] inUors, DgnGCS destDgnGCS);   // + overload returning lat/longs, + 2D
  int GetLocalTransform(out DTransform3d outTransform, DPoint3d elementOrigin, bool doRotate, bool doScale, DgnGCS destDgnGCS);
}
public class BaseGCS : IDisposable {                                            // Bentley.GeoCoord2.dll (selected)
  BaseGCS(); BaseGCS(string keyString);
  bool Valid; string Name {get;set;} string Description {get;set;} string Projection; ProjectionCodeValue ProjectionCode; string Units; int UnitCode;
  int EPSGCode {get;set;} int GetEPSGCode(bool dontSearch); int GetEPSGDatumCode(bool);
  string DatumName, DatumDescription, EllipsoidName; int DatumCode, EllipsoidCode; bool IsNAD27, IsNAD83, HasWGS84CoincidentDatum;
  double CentralMeridian, OriginLatitude, OriginLongitude, FalseEasting, FalseNorthing, ScaleReduction, Azimuth, StandardParallel1, StandardParallel2; int UTMZone, Hemisphere, Quadrant;
  int LocalTransformType {get;set;}  LocalTransformParams LocalTransformParameters {get;set;}   // struct { double A, B, C, D, E; } Helmert
  double GeoidSeparation, ElevationAboveGeoid; bool ReprojectElevation; string VerticalDatumName; VerticalDatumUICode VerticalDatumUserInterfaceCode;
  int InitFromEPSGCode(int epsg); int InitFromWellKnownText(string wkt); int GetWellKnownText(out string wkt, WellKnownTextFlavor flavor, bool originalIfPresent); int ToJson(out string); int FromJson(ref string, out string err);
  int LatLongFromCartesian(out GeoPoint, ref DPoint3d); int CartesianFromLatLong(out DPoint3d, ref GeoPoint); int LatLongFromLatLong(out GeoPoint, ref GeoPoint, BaseGCS dest);
  double GetConvergenceAngle(ref GeoPoint p); double GetGridScale(ref GeoPoint p); int GetCenterPoint(out GeoPoint); int GetDistance(out double dist, out double azimuth, ref GeoPoint a, ref GeoPoint b);
  bool IsEquivalent(BaseGCS other); bool HasEquivalentDatum(BaseGCS other); bool Validate(out string[] errors);
}
// enums: LocalTransformType { TRANSFORM_None, TRANSFORM_Helmert, TRANSFORM_SecondOrderConformal }; GeoCoordinationState { NoGeoCoordination=0, Reprojected=1, AECTransform=2 }
```

C++ doc: `CartesianFromUors` — "cartesian coordinates in the units specified by the GCS from design coordinates (UORs)" (projected CRS coordinate, GO accounted for). `GetLocalTransform` — "best approximate transform that can be applied at the elementOrigin to transform coordinates from this GCS's design coordinates to those of the destination GCS" (UOR → UOR; this is the *AEC Transform*). `ToModel(writeToFile:false)` = cache only; `reprojectData:false` = the UI "Correct" choice. Missing from managed `DgnGCS` vs C++/Python: `CreateGCS` statics, `DeleteFromModel`, `ReloadGeoReferences`, `FromCache`, `GetLinearTransformToBaseGCS`, `GetLinearTransformToECEF`, `GetLocalTransformer/SetLocalTransformer`. Deleting a GCS from managed code is only possible via COM `ModelReference.DeleteGCS(true)`.

Read:

```csharp
using Bentley.GeoCoordinatesNET;
DgnGCS gcs = DgnGCS.FromModel(model, primaryCoordSys: true);
if (gcs != null && gcs.Valid) {
  string name = gcs.Name; gcs.GetWellKnownText(out string wkt, BaseGCS.WellKnownTextFlavor.wktFlavorOGC, false);
  int epsg = gcs.GetEPSGCode(dontSearch: false);            // 0 if none
  gcs.CartesianFromUors(out BG.DPoint3d en, goUor);         // Easting/Northing/Elev of the GO in gcs.Units
  gcs.LatLongFromUors(out BG.GeoPoint ll, goUor);           // lon/lat/elev of the GO
  double convergenceDeg = gcs.GetConvergenceAngle(ref ll);  // grid north vs true north at that point
  bool helmert = gcs.LocalTransformType == 1; LocalTransformParams p = gcs.LocalTransformParameters;
}
```

Write:

```csharp
var baseGcs = new BaseGCS("EPSG:25832");                    // or new BaseGCS(); baseGcs.InitFromEPSGCode(25832); / InitFromWellKnownText(wkt)
baseGcs.LocalTransformType = 1;                              // Helmert
baseGcs.LocalTransformParameters = new LocalTransformParams { A = 1/scaleFactor, B = 0, C = dx, D = dy, E = dz };  // A..E ↔ a,b,c,d,e mapping unverified
var dgnGcs = new DgnGCS(baseGcs, model);
int status = dgnGcs.ToModel(model, primaryCoordSys: true, writeToFile: true, reprojectData: false, showProblems: true);
```

Forum C# snippet (Bentley Communities) using the same calls:

```csharp
DgnGCS cs = DgnGCS.FromModel(Session.Instance.GetActiveDgnModel(), true);
ModelInfo mInfo = Session.Instance.GetActiveDgnModel().GetModelInfo();
double scale = mInfo.UorPerMaster; DPoint3d offset = mInfo.GlobalOrigin;
ACSManager.GetActive(Session.GetActiveViewport()).GetOrigin(out DPoint3d acsOriginUor);
var acsOrigin = new DPoint3d((acsOriginUor.X - offset.X) / scale, (acsOriginUor.Y - offset.Y) / scale, (acsOriginUor.Z - offset.Z) / scale);
cs.LatLongFromCartesian(out GeoPoint acsLatLng, acsOrigin);   // reported ~100 m off on BNG → use LatLongFromUors instead
```

COM: `GeographicCoordinateSystem { Name, Description, ProjectionName, Units, DatumName, VerticalDatumName, PaperScale, LatLongFromMasterUnits(ref Point3d), MasterUnitsFromLatLong(ref GeoPoint3D), CartesianFromLatLong, LatLongFromCartesian, GetGridScale, GetDistance, SetPaperScale(...), IsEquivalent, HasEquivalentDatum }`; `ModelReference.GetGCS(true)` / `WriteGCS(gcs, true, reproject)` / `DeleteGCS(true)`; `Application.CreateGCSFromKeyName("VT83F")`. COM works in master units (GO-relative); managed works in UORs. VBA cannot read EPSG codes.

### 4.4 Reference attachments — `DgnAttachment : DgnModelRef`

```csharp
void GetTransformToParent(out DTransform3d transform, bool scaleZfor2dRef);   // already used by the gatherer
void GetMasterOrigin(ref DPoint3d origin); void SetMasterOrigin(DPoint3d);      // parent UORs
DPoint3d GetRefOrigin(); void SetRefOrigin(DPoint3d);                           // referenced-file UORs
DMatrix3d GetRotation(); void SetRotMatrix(DMatrix3d);
double StoredScale {get;set;} double DisplayScale {get;set;} ScaleMode GetScaleMode(); void SetScaleMode(ScaleMode);   // ScaleMode { TrueScale=0, StorageUnits=1, Direct=2 }
string AttachFileName, AttachModelName; string LogicalName {get;set;} string AttachDescription {get;set;} int NestDepth {get;set;} bool DoNotDisplayAsNested {get;set;}
bool IsDisplayed; void SetIsDisplayed(bool state, bool loadIfNecessary, bool processAffected);
bool IsMissingFile, IsMissingModel, IsMissingGeoCoordSys, IsMissingGeoCoordApp;
ElementId GetElementId(); DgnModelRef GetParent(); DgnAttachment GetParentDgnAttachment(); DgnAttachment GetBaseDgnAttachment();
StatusInt ApplyStandardView(StandardView, double userScale, double acsScale); StatusInt ApplyNamedView(...);
StatusInt Rewrite(bool writeSettings, bool writeLevelDisplaySettings);          // after any Set*
StatusInt WriteToModel(bool loadRasterRefs);                                    // after DgnModelRef.CreateDgnAttachment(DgnDocumentMoniker, string modelName)
// DgnModelRef: DgnAttachmentCollection GetDgnAttachments(); ReadAndLoadDgnAttachments(DgnAttachmentLoadOptions); FindDgnAttachmentByElementId(ElementId); DeleteDgnAttachment(DgnAttachment)
```

Storage: element type **100** `ReferenceAttachment` (+108 `ReferenceOverride`); parameters also exposed as EC properties (`MstnReferenceAttachment` in `BaseElementSchema.01.00`). Coincident-World is not a managed flag; it is realised through master/ref origin values (COM `Attachments.AddCoincident1(..., MsdAddAttachmentFlags.CoincidentWorld=2)`).

COM `Attachment : ModelReference`: `MasterOrigin` (master units), `AttachmentOrigin` ("returned in UOR's, not master units"), `Rotation`, `ScaleFactor` ("product of the requested scale times the factor needed to achieve True Scale"), `ScaleMasterUnits`, `ScaleStored`, `IsTrueScale`, `GlobalOrigin` (of the attached model), `GetMasterToReferenceTransform()` / `GetReferenceToMasterTransform()` (help text is self-contradictory about direction), `Move`, `ScaleUniform`, `Transform`, `Rewrite`; `Attachments.Add(file, model, logical, description, out masterOrigin /*MU*/, out refOrigin /*UOR*/, trueScale, displayImmediately)`, `AddCoincident`, `AddCoincident1(..., flags)`.

### 4.5 North and solar

`ModelInfo.Azimuth` (§4.1); `SolarLight : AdvancedLight { SolarType {TimeLocation=0, Direction=1}, double AzimuthAngle, double AltitudeAngle, GeoPoint2d GeoLocation, double GmtOffset, uint Year/Month/Day/Hour/Minute, bool UseDaylightSavings, DPoint3d VectorOverride, DVector3d GetEffectiveVector(DgnModel) }`; `static class SolarUtility { DirectionToAzimuthAngles(out az, out alt, DPoint3d dir, DgnModel); DVector3d GetSolarDirection(y,m,d,h,min,dst,gmt,GeoPoint2d,DgnModel,out az,out alt); }`; `LightManager.GetActiveLightSetupForModel(bool useModelLighting, DgnModel)`, `LightManager.FindLightsInModelRef(DgnModelRef)`. The Light Manager "True North Direction" is not separately exposed **[unverified whether it reads `ModelInfo.Azimuth`]**.

### 4.6 Version notes

MicroStation 2026 vs ORD 2025: identical public surface for the classes above. .NET Framework 4.8 target (2024–2026 SDKs), "Breaking Changes – N/A". C++ 2024 SDK added `DgnGCS::GetLinearTransformECEF` and `DgnAttachment::IsUntransformedAttachment` (not in managed). COM `GeographicCoordinateSystem.PaperScale/SetPaperScale` are marked "Version 26.00.01" (new in 2026).

---

## 5. ODA Drawings C++ API — read and write, per concept

SDK: `C:\dev\speckle-converters\external\ODA\Drawings_vc18_amd64dll_27.5\` (`<ODA>`), headers under `Dgn\include\`. Header doc comments are mostly inline `//`; quoted verbatim where present. **[all verified from headers/examples]**

### 5.1 Working units — `DgModel.h`

```cpp
// DgModel.h:77-84
enum WorkingUnit { kWuUnitOfResolution = 0, kWuStorageUnit = 1, kWuWorldUnit = 2 /*meters*/, kWuMasterUnit = 3, kWuSubUnit = 4 };
// DgModel.h:122-160
struct StorageUnitDescription { UnitBase m_base; UnitSystem m_system; double m_numerator, m_denominator; double m_uorPerStorageUnit; };
struct UnitDescription        { UnitBase m_base; UnitSystem m_system; double m_numerator, m_denominator; OdString m_name; };
// DgModel.h:493-550
WorkingUnit getWorkingUnit() const;                       void setWorkingUnit(WorkingUnit);
void getStorageUnit(StorageUnitDescription&) const;      void setStorageUnit(const StorageUnitDescription&);
void getMasterUnit(UnitDescription&) const;              void setMasterUnit(const UnitDescription&, bool bReScaleGeometry = true);
void getSubUnit(UnitDescription&) const;                 void setSubUnit(const UnitDescription&);
static void fillUnitDescriptor(UnitMeasure, UnitDescription&);
double getMeasuresConversion(WorkingUnit from, WorkingUnit to) const;
double convertUORsToWorkingUnits(double) const;  double convertWorkingUnitsToUORs(double) const;   // + point/vector overloads
```

Semantics: the "working unit" is a per-model *view mode*. With `kWuMasterUnit` (the norm) every geometry getter already returns master units; `getMeasuresConversion(kWuUnitOfResolution, getWorkingUnit())` converts fields still stored in UORs (reference origin, insertion base, text heights). There is no `getUORPerMeter()`: UOR/m = `getMeasuresConversion(kWuWorldUnit, kWuUnitOfResolution)` or `m_uorPerStorageUnit * m_numerator / m_denominator`. Sample setup (`ExDgnCreate\ExDgnFiller.cpp:242-253`): `fillUnitDescriptor(kMeters, d); setMasterUnit(d); fillUnitDescriptor(kMillimeters, d); setSubUnit(d); setWorkingUnit(kWuMasterUnit);`.

### 5.2 Global Origin — `DgModel.h`

```cpp
// DgModel.h:389-391
// The global origin defines the start position (zero point) of the world coordinate system inside of the model space
OdGePoint3d getGlobalOrigin() const;   void setGlobalOrigin(const OdGePoint3d& origin);
// DgModel.h:561-579
/** The function return status of global origin support mode. */
virtual bool isGlobalOriginEnabled() const;
/** If bEnable is true, the function call transformBy(...) for the model to global origin without undo recording and set
    flag to recalcualte elements when global origin is changed and restore element positions before of write to file.
    If bEnable is false, the function call transformBy(...) for the model disable global origin offset and reset flag. */
virtual void enableGlobalOriginUsage(bool bEnable = true);
// also: getInsertionBase()/setInsertionBase() (479-480, "Only in case a model is placed as a cell"), getSolidExtent()
```

Rule: **world (MicroStation readout) = stored_working_units − getGlobalOrigin()** when `!isGlobalOriginEnabled()`; if enabled, ODA has already shifted the model in memory. ODA's own DGN→DWG importer does `transformBy(translation(-getGlobalOrigin()))` on every entity and subtracts the GO from camera positions (`DgnImportTables.cpp:1797-1839, 3884-3905`). Write sample (`OdaDgnAppDoc.cpp:4957-4966`): `pModel->setGlobalOrigin(ptNewGO + pModel->getGlobalOrigin().asVector());`. RX properties: `OdDgModelGlobalOriginProperty`, `OdDgModelIsGlobalOriginEnabledProperty` (`DgTableProperties.cpp:21985-22004`).

### 5.3 Other model settings

`getType() {kDesignModel, kSheetModel, kExtractionModel, kDrawingModel}`, `getModelIs3dFlag()`, grid (`getGridBase()`, `getGridAngle()`, `getGridRatio()`, `getGridOrientation() {kView, kTop, kRight, kFront, kACS}`), angle readout (`getAngleDirectionMode() {kAzimut=1, kBearing=2}`, `getAngleDirectionClockwiseFlag()`, `getAngleReadoutDirectionBase()`, `getAngleReadoutDirection()`), `getAnnotationScale()`; sheet models `getSheetUnits()/getSheetOffset()/getSheetRotation()` (`DgModel.h:690-766`). No `getTransformation`/`getModelToWorld`.

### 5.4 ACS — `DgModel.h`, `DgACS.h`, `DgView.h`

```cpp
// DgModel.h:66-75
enum AcsType { kGeographic = 0, kRectangular = 1, kCylindrical = 2, kSpherical = 3, kMilitaryGrid = 4, kMilitaryGridWGS84 = 5 };
// DgModel.h:376-387 — active ACS of the model
AcsType getAcsType() const;  OdGePoint3d getAcsOrigin() const;  OdGeMatrix3d getAcsRotation() const;  OdDgElementId getAcsElementId() const;   // + setters
// DgACS.h:39-61 — named ACS element (control element; enumerate via createControlElementsIterator() + isKindOf(OdDgACS::desc()))
class OdDgACS : public OdDgElement {
  static OdDgACSPtr createGeographicalACS(const OdDgModel*);  static OdDgACSPtr createMilitaryGridACS(const OdDgModel*, bool bWGS84 = false);
  OdString getName()/getDescription();  OdGePoint3d getOrigin();  OdGeMatrix3d getRotation();  OdDgModel::AcsType getType();   // + setters
};
// DgView.h:177-193 — active ACS of a view
OdDgElementId getAcsId() const;  void applyAcs(const OdDgElementId&);  void applyAcs(const OdDgACS*);
OdGePoint3d getAcsOrigin() const;  OdGeMatrix3d getAcsRotation() const;  bool getAcsViewIndependentFlag() const;  OdDgModel::AcsType getAcsType() const;
// persisted: OdDgAcsXAttribute (DgXAttribute.h:1904-1945, kType = 0x57170001)
```

Create sample (`ExDgnFiller.cpp:1455-1466`): `pACS = OdDgACS::createObject(); setName; setOrigin; setRotation(OdGeMatrix3d::rotation(OdaPI/4, kZAxis)); setType(kCylindrical); m_pModel3d->addElement(pACS);`.

### 5.5 GCS — `DgGeoData.h` (3204 lines) + extension module `DgGeoDataEx`

```cpp
// DgModel.h:558-559
virtual OdDgGeoDataInfoPtr getGeoData(OdDg::OpenMode = OdDg::kForRead) const;
virtual void setGeoData(OdDgGeoDataInfo*, const OdDgGeoDataReprojectionSettings&, OdDgGeoDataCoordinateSystemChangeAction = kNoAction);
enum OdDgGeoDataCoordinateSystemChangeAction { kNoAction = 0, kChangeStorageUnits = 1, kChangeMasterUnits = 2, kReprojectCoordSystem = 3 };
// DgGeoData.h:2924-3069
class OdDgGeoDataInfo : public OdDgElement {
  enum { kSignature = 0x00B6 };
  enum OdDgGeoDataVerticalDataType { kMatchDatum = 0, kGeoid = 1 };
  enum OdDgGeoDataLocalTransformType { kNoTransform = 0, kHelmertTransform = 1 };
  static OdDgGeoDataInfoPtr createObject(const OdString& coordSysIdOrFullDef);   // CS-Map key or WKT
  static OdDgGeoDataInfoPtr createObject(const OdDgGeoDataCoordinateSystemPtr&);
  static OdResult createAll(const OdGePoint3d& geoPt /*lon,lat,alt*/, OdArray<OdDgGeoDataCoordinateSystemPtr>&);
  static OdDgGeoDataInfoPtr createObject(const OdArray<OdDgKMLPlacemark2dPtr>&);  // Azimuthal Equal Area from placemarks
  OdDgGeoDataCoordinateSystemPtr getCoordinateSystem() const;  OdResult setCoordinateSystem(...);  OdResult setCoordinateSystemByName(const OdString&);
  OdDgGeoDataDatum getDatum() const;  OdResult setDatum(const OdString&);  OdDgGeoDataEllipsoid getEllipsoid() const;
  OdResult getWktRepresentation(OdString& strWkt) const;
  OdDgGeoDataVerticalDataType getVerticalDataType() const;  OdDgGeoDataLocalTransformType getLocalTransformType() const;
  OdDgGeoDataHelmertParams getHelmertParams() const;  void setHelmertParams(const OdDgGeoDataHelmertParams&);
  OdUInt32 getNumberOfSubUnitsInMasterUnit() const;  OdUInt32 getNumberOfUORSInSubUnit() const;
  bool getPlacemarkSourceFlag() const;
};
struct OdDgGeoDataHelmertParams { double m_dParamA /*s·cos r*/; double m_dParamB /*s·sin r*/; OdGePoint3d m_ptOffset /*c,d,e*/; };
// DgGeoData.h:3072-3128 — services (protocol extensions)
class OdDgGeoDataCoordinateConverter { transformXYZPointLL(const OdGePoint3d& xyz, OdGePoint3d& ll); transformLLPointXYZ(...); /* + arrays */ };
class OdDgGeoDataReprojectionCoordinateTransformer { transformPoint(OdGePoint3d&); transformPoints(OdGePoint3dArray&); };
class OdDgGeoDataPE : public OdRxObject {
  OdDgGeoDataReprojectionCoordinateTransformerPtr createReprojectionTransformer(const OdDgGeoDataInfo* from, const OdDgGeoDataInfo* to, double dModelScale = 1.0, bool bReprojectEllevation = true);
  OdDgGeoDataCoordinateConverterPtr createGeoDataCoordinateConvertor(const OdDgGeoDataInfo*, const OdDgModel* = NULL);
  OdResult getWktRepresentation(const OdDgGeoDataInfo*, OdString&) const;  /* + getGeoDataByName, createAll, transformGeoPointByDatum, createGeoPointFromCS */
};
// Kernel: OdDbBaseGeoDataExportPE::getGeoDataParams(pDb, OdString& wkt, int& type /*0 geographic, 1 projected*/, int& epsgCode)  — DGN impl NEVER fills epsgCode (OdDgGeoDataPEImpl.cpp:1113-1137)
```

Coordinate system base (`DgGeoData.h:254-443`): `getProjectionType()` (80 CS-Map codes, e.g. `kTransverseMercator=3`, `kLambertConformalConic=37`, `kUniversalTransverseMercator=44`, `kPseudoMercator=69`), `getName/getGroupName/getDescription/getSource`, `getUnits()` (`kUnitMeter=1, kUnitFoot=2, … kUnitDegree=1001`), `getGeodeticExtents()`, `getExternalData()` → `{ m_dOrgLatitude, m_dOrgLongitude, m_dScaleReduction, m_dFalseEasting, m_dFalseNorthing, … }`; ~80 typed subclasses. Datum (`2717-2798`): conversion method, offset, rotations, scale, `getTransformMatrix()`. KML placemarks (`DgKMLPlacemark.h`): `getLongitude/getLatitude` (**radians**), `getElevation`, `getOrigin()` (model coords), `createPlacemarkByCoord(...)`.

Module loading (required; `DgGeoDataEx` ships as source under `Dgn\Extensions\DgGeoDataEx\` and must be built as a `.tx`; it loads Kernel `OdSpatialReference`):

```cpp
::odrxDynamicLinker()->loadModule(L"TG_Db", false);
OdRxModulePtr pGeo = ::odrxDynamicLinker()->loadModule(L"DgGeoDataEx", true);   // ExDgnCreate.cpp:244-247
```

Canonical read (ODA's own `OdDgGeoDataPEImpl.cpp:1113-1137, 1228-1233`; `DgnImportGeoData.cpp:93-181`):

```cpp
OdDgModelPtr pModel = pDb->getActiveModelId().safeOpenObject();
OdDgGeoDataInfoPtr pGeo = pModel->getGeoData(OdDg::kForRead);
if (!pGeo.isNull()) {
  OdString wkt; pGeo->getWktRepresentation(wkt);
  OdDgGeoDataCoordinateSystemPtr pCs = pGeo->getCoordinateSystem();      // pCs->getProjectionType(), getUnits(), getName()
  OdDgGeoDataPEPtr pPE = OdDgGeoDataPE::cast(pGeo.get());
  OdDgGeoDataCoordinateConverterPtr conv = pPE->createGeoDataCoordinateConvertor(pGeo, pModel);
  OdGePoint3d ll; conv->transformXYZPointLL(OdGePoint3d(x, y, 0.) /*master units*/, ll);   // ll = (lon°, lat°, elev)
  // Helmert: pGeo->getLocalTransformType() == kHelmertTransform → pGeo->getHelmertParams()
}
```

Converter internals: input XYZ expected in **master units**; scales by `masterUnits.m_denominator/m_numerator / csUnitScale`, applies Helmert if set, then CS-Map `convertToLonLat` (note the `.y` bug at `OdDgGeoDataPEImpl.cpp:74` — don't copy). Write (`DgnExportImpl.cpp:1288-1296, 1385-1393`): `OdDgGeoDataInfo::createObject(wkt)` → `pModel->setGeoData(pGeo, OdDgGeoDataReprojectionSettings())`; Helmert offset via `setLocalTransformType(kHelmertTransform)` + `setHelmertParams(...)`.

### 5.6 Reference attachments — `DgReferenceAttach.h`

```cpp
// DgReferenceAttach.h:279-312
// Note: this point is managed as UORs, because it is an offset of zero point within the referenced file
OdGePoint3d getReferenceOrigin() const;   void setReferenceOrigin(const OdGePoint3d&);
// it is an offset of the referenced file (relative to its zero point) within the current file
OdGePoint3d getMasterOrigin() const;      void setMasterOrigin(const OdGePoint3d&);
// this matrix is for rotation only
OdGeMatrix3d getTransformation() const;   void setTransformation(const OdGeMatrix3d&);
// low-level scale factor ... It is recommended to use getEntireScale().
double getScale() const;                  void setScale(double);
// ratio: 'working units of a referenced model' / 'working units of the current model'
double getEntireScale() const;
double getZFront()/getZBack();  OdGePoint3d getCameraPosition();  double getCameraFocalLength();
// flags 336-404: getCoincidentFlag, getDisplayFlag, getCameraOnFlag, getTrueScaleFlag, getViewportFlag, getScaleByStorageUnitsFlag, getDoNotDisplayAsNestedFlag, getMissingFileFlag, getInactiveFlag, getClipFront/BackFlag, getViewFlags(uViewIndex)
// resolution 414-430: OdRxObjectPtr getReferencedDatabase() (TG or TD database); OdDgModelPtr getReferencedModel() (DGN only)
// getNestDepth(), getBaseNestDepth(), getLogicalName(), getModelName(), getFileName(), getRefModelIndex() (2027.3)
```

Canonical composed transform (`ExDgnRecomputeAssocPoints\DgRecomputeAssocPtsPEImpl.cpp:57-115`, same in `DgnImportXRef.cpp:1310-1333`, `OdaDgnAppDoc.cpp:761-779`):

```cpp
OdGeMatrix3d m = OdGeMatrix3d::translation(-(ref->getReferenceOrigin() + (viewport ? OdGeVector3d() : model->getInsertionBase().asVector())).asVector()
                                            * model->getMeasuresConversion(OdDgModel::kWuUnitOfResolution, model->getWorkingUnit()));
m = OdGeMatrix3d::scaling(ref->getEntireScale()) * m;
m = ref->getTransformation() * m;                 // repair if singular
m = OdGeMatrix3d::translation(ref->getMasterOrigin().asVector()) * m;
```

No `getRotationMatrix`, `getTransformationToWorld`, or any geo-reprojection flag exist on the header; geographic attachments must be handled via `OdDgGeoDataPE::createReprojectionTransformer(refGeo, masterGeo, modelScale)` per point.

### 5.7 File header (TCB) — `DgDatabase.h`

`getAzimuth()/setAzimuth()` (azimuth true north angle), `getLatitude()/getLongitude()` (`OdAngleCoordinate` DMS ints), `getSolarDirection()`, `getSolarFlag()`, `getGMTOffset()`, `getActiveModelId()`, `getDefaultModelId()`, `getActiveViewGroupId()`, `getUnitsAccuracy()/getUnitsFormat()` (2027.3). **No GO, ACS or GCS on the database** — all per model.

### 5.8 DWG analogue for comparison — `DbGeoData.h`

`OdDbGeoData`: `coordinateType() {kCoordTypLocal, Grid, Geographic}`, `designPoint()`, `referencePoint()`, `northDirection()` ("azimuth of the Y axis relative to true north in radians east of north"), `horizontalUnitScale()`, `coordinateSystem()` (WKT), `transformToLonLatAlt(...)`. `OdDbGeoCoordinateSystem::getEpsgCode()`. The DGN model is richer in CRS parameters (full CS-Map + Helmert) but lacks a first-class design-point/reference-point pair — DGN expresses that via the Helmert offset or a KML placemark (`DgnExportImpl.cpp:1372-1393`, `DgnImportGeoData.cpp:34-89`).

---

## 6. Current state of our code and gaps

### 6.1 C# connector (`Converters/Bentley/…MicroStationShared`, `Connectors/Bentley/…MicroStationShared`)

| Item | State | Where |
|---|---|---|
| Settings record | `MicroStationConversionSettings(ActiveModel, SpeckleUnits, UorPerMaster, GlobalOriginX/Y/Z, IncludeReferenceAttachments)` — GO in UORs | `Settings/MicroStationConversionSettings.cs:348-356` |
| Only place model metadata is read | `info = activeModel.GetModelInfo(); info.GetMasterUnit(); info.GlobalOrigin; info.UorPerMaster` | `Settings/MicroStationConversionSettingsFactory.cs:374-389`, called from `Bindings/MicroStationSendBinding.cs:68-77` |
| Coordinate choke point | `GeometryMapper.MapXyz` (81-96): ambient transform → `(p − GO) * 1/UorPerMaster`, GO subtract suppressed when `InDefinitionFrame`; `ToSpeckleMatrix` (148-172) / `ToInstanceMatrix` (178-199): translation `(t − GO) * scale` only for top-level placements; `MapDirection`/`MapVectorRaw` linear part only | `Services/GeometryMapper.cs` |
| Units | `MicroStationToSpeckleUnitConverter` maps `UnitDefinition` → Speckle unit; **US survey foot collapsed into feet** (2 ppm ≈ 6 ft at 3 000 000 ft on state-plane files) | `Services/MicroStationToSpeckleUnitConverter.cs:271-327` |
| Reference attachments | `CollectAttachments` walks `GetDgnAttachments()` recursively (depth ≤ 16, ≤ 4096), skips missing/undisplayed, composes `GetTransformToParent(out toParent, true)`; geometry ends in the **master model's UOR frame, then master GO subtracted** (child GOs irrelevant; matches dgnextract) | `Operations/Send/MicroStationElementGatherer.cs:59-166` |
| Existing card setting | `SendIncludeReferencesSetting` (`Type="boolean"`), `MicroStationSendSettings.GetIncludeReferences(card)` | `Operations/Send/MicroStationSendSettings.cs:11-30`, `MicroStationSendBinding.cs:39` |
| Probe keyin | logs `uorPerMaster`, `globalOrigin (uor)`, model range, per-attachment `GetTransformToParent` translation; **nothing about ACS, GCS, azimuth, master/ref origins** | `Plugin/ProbeCommand.cs:493-501, 598-624` |
| csproj references | `Bentley.DgnPlatformNET`, `Bentley.GeometryNET(.Structs)`, `Bentley.Interop.MicroStationDGN`, `Bentley.ECObjects3`, `Bentley.Platform`, `ustation`; **no `Bentley.DgnGeoCoord2` / `Bentley.GeoCoord2`** | both `*2026.csproj` |
| Metadata | no `modelPlacement.*` / `geolocation.*` / `crs.*` rows; bundle builder uses the SDK `BundleBuilder` without model-property calls | `Operations/Send/MicroStationBundleBuilder.cs` |

So today the connector implicitly sends the **Global Origin** frame (Revit "Project Base Point" equivalent) with `appliedToGeometry = true`, and records nothing.

### 6.2 dgnextract (`C:\dev\speckle-converters\speckle-converters\native\dgnextract\src`)

| Concept | Status | Where |
|---|---|---|
| Unit label | base model's master (or sub) unit → Speckle unit; non-metre bases → `"m"` | `main.cpp:92-113, 697-702, 733-740` |
| UOR scaling | none for geometry (getters return working units); `getMeasuresConversion(kWuUnitOfResolution, wu)` for ref origin / insertion base | `main.cpp:180-184` |
| Global origin | base model's `getGlobalOrigin()` subtracted after ambient transform; suppressed in definition frames; instance translations minus GO | `main.cpp:694-696, 727-732, 859-860`; `dgn_common.h:139-172` |
| `isGlobalOriginEnabled()` | **not checked** (double-subtract risk if ever enabled) | — |
| Attachment transform | full ODA formula incl. insertion base / viewport flag / entireScale / singular-rotation repair; cycle guard, depth ≤ 16 | `main.cpp:162-251` |
| Attachment display/clip/nest flags | not consulted (hidden/clipped refs extracted) | — |
| ACS (model, view, elements) | not read | — |
| GCS (`getGeoData`, WKT, datum, Helmert, placemarks) | **not read; `DgGeoDataEx` not loaded** | — |
| Views (camera, level masks) | only `OdDgViewGroup::getName/getModelId` for base-model resolution | `main.cpp:253-275` |
| Sun / azimuth / lat-long | not read | — |
| `modelPlacement.*` / `geolocation.*` / `crs.*` rows | **none** (contrast rvextract `placement_revit.h:188-245`) | — |

The retired managed oracle deliberately avoided `enableGlobalOriginUsage(true)` because it "can't affect the bytes the STL exporter writes" (`DgnUnitsProvider.cs:15-17`); unverified against 27.5.

---

## 7. Recommended design for the MicroStation reference point feature

### 7.1 Frames and transforms

Define the connector's **internal frame** as design-plane coordinates scaled to master units: `P_int = P_uor / uorPerMaster` (Revit "internal" analogue). Every option is a rigid (or similarity) transform **INTERNAL → datum**, stored exactly like Revit's `modelPlacement.transform`:

| Option (enum) | Revit analogue | Transform INTERNAL → datum | Source |
|---|---|---|---|
| `DesignPlaneOrigin` | InternalOrigin | identity | — |
| `GlobalOrigin` (**default**, current behaviour) | ProjectBase | `T(−GO / uorPerMaster)` | `ModelInfo.GlobalOrigin` |
| `ActiveAcs` | Survey / SharedCoordinates | `S(1/scale) · Rᵀ? · T(−ACS.origin / uorPerMaster)` (rotation convention to verify; offer Rectangular only) | `ACSManager.GetActive(vp)` |
| `NamedAcs:<name>` (optional, list from `Traverse`) | SharedCoordinates | same, per named ACS | `ACSManager.GetByName` |
| `GeographicCoordinateSystem` (enabled only when `DgnGCS.FromModel` is valid) | SharedCoordinates + SiteLocation | affine fitted from `CartesianFromUors` sampled at the origin and three axis points (unit conversion + Helmert; check linearity residual), then converted from `gcs.Units` to Speckle units | `DgnGCS` |

Notes:
- `GlobalOrigin` reproduces today's output bit-for-bit (dgnextract parity), so it must stay the default.
- `ActiveAcs` is per view; capture at send time and record the ACS name; treat `Type == None` or non-rectangular as "no ACS" and fall back with an `…Fallback` marker like Revit's `internalOriginFallback`.
- For the GCS option, the general mapping is a map projection, but for a projected CRS with matching units it is exactly linear; if the fitted affine's residual exceeds tolerance (geographic lat/long GCS, paper scale ≠ 1), disable the option and only emit metadata.
- **Apply Transform** boolean exactly as Revit/AutoCAD: when off, converters see `ReferencePointTransform = null` and geometry is sent in the internal frame with `appliedToGeometry=false`. **Decision needed**: which frame counts as "internal". Two candidates: (a) the design plane (true storage frame; matches Revit, iModel and ODA semantics; but "Apply Transform off" would then emit raw UOR-centred coordinates, unlike today), or (b) the GO frame (what dgnextract and the connector send today; then `DesignPlaneOrigin` becomes `T(+GO)`). Recommendation: (a) for semantic parity with Revit, with `GlobalOrigin` as the default option and `applyTransform` defaulting to **true** for MicroStation (unlike Revit's false), so existing bundles stay bit-identical and the datum choice becomes visible in metadata.

### 7.2 Code changes (mirroring Revit)

Converter layer (`Converters/Bentley/Speckle.Converters.MicroStationShared`):
1. `Settings/MicroStationReferencePointType.cs` — enum above.
2. Extend `MicroStationConversionSettings` with `BG.DTransform3d? ReferencePointTransform`, `bool ApplyTransform`, `MicroStationReferencePointType ReferencePointKind`, `BG.DTransform3d? ModelReferencePointTransform` (Revit quartet); factory nulls the first when `!applyTransform`, keeps the last for metadata; reference the two GeoCoord DLLs (`HintPath $(MicroStation2026Dir)Bentley.DgnGeoCoord2.dll` / `Bentley.GeoCoord2.dll`, `Private=False`).
3. Apply in the single choke point: `GeometryMapper.MapXyz` (replace the GO subtract by `Multiply(ReferencePointTransform, p)` in UOR **before** scaling, only when `!InDefinitionFrame`), `ToSpeckleMatrix`/`ToInstanceMatrix` (pre-multiply top-level placement), `MapDirection`/`MapVectorRaw` (rotation part when the frame rotates). Definition-frame suppression keeps applying to the translation only.
4. A `MicroStationReferencePointResolver` service that builds all option transforms + metadata (GO, UOR/m, units, ACS list, GCS name/EPSG/WKT/units/Helmert/paper scale, azimuth, lat/long of GO via `LatLongFromUors`, convergence angle).

Connector layer (`Connectors/Bentley/Speckle.Connectors.MicroStationShared`):
5. `SendReferencePointSetting` (`Type="string"`, `SETTING_ID="referencePoint"`, `Enum` from the enum, filtered at construction: drop `GeographicCoordinateSystem` when the model has no GCS, like AutoCAD's `includeGridCoordinates`) and `SendApplyTransformSetting` (`Type="boolean"`, id `applyTransform`) in `Operations/Send/MicroStationSendSettings.cs`; add to `MicroStationSendBinding.GetSendSettings()` and resolve in `InitializeConverterSettings`.
6. Per-card transform cache with `sendConversionCache.EvictObjects(...)` when the resolved transform changes (GO/ACS can change between sends); also invalidate on `ModelInfo.ModelInfoChanged` and `AddIn.AcsOperationEventHandler`.
7. Metadata rows (same keys as Revit so viewer/receivers treat them identically):
   - `modelPlacement.options.{designPlaneOrigin|globalOrigin|activeAcs|geographicCoordinateSystem}.transform` (16 row-major CSV, translation in Speckle units, `"R"`), `modelPlacement.default/transform/units/source/appliedToGeometry`.
   - `referencePoints.globalOrigin.position.{x,y,z}` (design-plane position of the GO in Speckle units), `referencePoints.activeAcs.{name,type,scale}`, `.origin.{x,y,z}`, `.rotation` (9 values).
   - `projectLocation.trueNorthAngle` from `ModelInfo.Azimuth` (units `"deg"`; convert to `"rad"` for parity with Revit) and `projectLocation.gridConvergenceAngle` from `GetConvergenceAngle` when a GCS exists.
   - `geolocation.anchor.latitude/longitude/elevation/source="gcs"`, `.referencePoint="globalOrigin"`.
   - `crs.horizontal.authority="EPSG"` + `code` from `GetEPSGCode(false)` (0 → omit), `nativeCode` = CS-Map key name, `definition` = WKT, `crs.units`, plus `coordinateOperation.localToGrid.*` from the Helmert parameters (A, B, C, D, E) as Civil3D does.
   - `microstation.uorPerMeter`, `microstation.uorPerMaster`, `microstation.storageUnit` (diagnostics for precision).
   Verify the MicroStation `IBundleBuilder` path can call the SDK's `AddModelProperty(key, value, units)` (Revit uses `ObjectsArtifactPipeline`).
8. Probe keyin: dump ACS (active + named), GCS summary, azimuth, attachment master/ref origins so the live checkpoint validates the conventions in §8.

dgnextract parity (optional, C++): load `DgGeoDataEx`, read `getGeoData()` + Helmert + placemarks, model active ACS, `isGlobalOriginEnabled()` guard, emit the same `modelPlacement.*`/`geolocation.*`/`crs.*` rows.

### 7.3 Receive (future)

`Source | DesignPlaneOrigin | GlobalOrigin | ActiveAcs | GeographicCoordinateSystem`; invert the same transforms after unit scaling (Revit order). With a target GCS and incoming `crs.*`/`geolocation.*`, prefer `DgnGCS.UorsFromLatLong` / `UorsFromCartesian` or one `GetLocalTransform` (AEC-style linear fit) over per-point reprojection; warn when results leave `ModelInfo.SolidExtent`. Writing datums back: `ModelInfo.GlobalOrigin` + `SetModelInfo` + `SaveModelSettings`; ACS via `CreateACS`/`SaveToFile`/`SetActive`; GCS via `new DgnGCS(baseGcs, model).ToModel(...)`.

---

## 8. Verification checklist (probe keyin) and open questions

Live checks for the next MicroStation session:

1. **GO sign/units**: on a file with a shifted GO, compare `GO=$` output with `info.GlobalOrigin / UorPerMaster` and confirm `readout = (UOR − GO)/uorPerMaster` (2D models included).
2. **`GetModelInfo()` mutability**: does `info.GlobalOrigin = …; SetModelInfo(info); SaveModelSettings()` persist without `MakeCopy`? Undo behaviour?
3. **ACS**: `GetActive(vp)` return when no ACS is defined (null vs `Type == None`); row/column convention of `GetRotation` (compare with the Auxiliary Coordinates dialog on a 45° ACS); meaning of `GetByName(..., options)` and `GetACSTraversalOptions()` bits; `Scale` semantics.
4. **GCS**: `FromModel` on a model without GCS (null vs `!Valid`); `CartesianFromUors` linearity and unit handling on a state-plane (US survey foot) file; `GetEPSGCode(false)` coverage for DOT `.dty` custom systems (expect 0); `LocalTransformParams.A..E` ↔ Helmert a, b, c, d, e; the BNG ~100 m `LatLongFromCartesian` defect in 2026.
5. **Attachments**: what `GetTransformToParent` returns for *Geographic – Reprojected* and *AEC Transform* attachments; whether Coincident vs Coincident-World with differing GOs matches MicroStation's display when only the master GO is subtracted.
6. **Azimuth**: `DEFINE NORTH` → `ModelInfo.Azimuth`? Light Manager "True North" relation? Convention (from +Y or +X, sign).
7. **Bundle**: can the MicroStation bundle path emit `eav.model` rows.
8. **Units**: decide on US survey foot handling before georeferenced files (currently collapsed into feet).

Open questions from the sources: exact `GO=$` convention in 2026; XM default GO location; legacy `ACS …` key-in spellings in CONNECT; `mdlACS_*` / `mdlModelRef_getGlobalOrigin` MDL signatures (SDK CHM not installed); GCS element type / XAttribute id in DgnPlatform terms (ODA: signature `0x00B6`); hidden non-accepted Bentley-staff forum answers (DgnGCS 100 m thread, DgnAttachment creation fix, VBA `GlobalOrigin` thread).

---

## 9. Sources

**Local, verified.** Repo: `Converters/Revit/Speckle.Converters.RevitShared/{Settings/ReferencePointType.cs, Settings/RevitConversionSettings.cs, Settings/RevitConversionSettingsFactory.cs, IReferencePointConverter.cs, ReferencePointConverter.cs, Helpers/ReferencePointHelper.cs, Helpers/DisplayValueExtractor.cs, ToSpeckle/Raw/Geometry/XyzConversionToPoint.cs}`, `Connectors/Revit/Speckle.Connectors.RevitShared/{Operations/Send/Settings/*.cs, Operations/Receive/*.cs, Bindings/RevitSendBinding.cs, Operations/Send/RevitArtifactRootObjectBuilder.cs, Operations/Receive/RevitHostObjectArtefactBuilder.cs}`, `Connectors/Autocad/…/ModelPlacementSettings.cs`, `Converters/Autocad/…/Helpers/ReferencePointHelper.cs`, `Connectors/Autocad/Speckle.Connectors.Civil3dShared/HostApp/Civil3dModelPlacement.cs`, `Sdk/Speckle.Converters.Common/ConverterSettingsStore.cs`, `DUI3/Speckle.Connectors.DUI/Settings/CardSetting.cs`, `Sdk/Speckle.Connectors.Common/Operations/RootKeys.cs`; MicroStation connector/converter files listed in §6.1; git history `6b7574e5f`, `7852b1d2f`, `4bb67318a`, `cd3037065`, `d05667dac`, `7428eb48b`, `bf2a08ba4`, `0726579ab`, `6018509b1`, `0313a8987`. dgnextract: `native/dgnextract/src/{main.cpp, dgn_common.h, geometry_dgn.h, curves_dgn.h, instances_dgn.h}`, `native/rvextract/src/placement_revit.h`, `native/core/envelope_catalog.h`. ODA 27.5: `Dgn/include/{DgModel.h, DgACS.h, DgView.h, DgGeoData.h, DgReferenceAttach.h, DgDatabase.h, DgXAttribute.h, DgKMLPlacemark.h}`, `Kernel/Include/DbBaseDatabase.h`, `Drawing/Include/DbGeoData.h`, `Dgn/Extensions/{DgGeoDataEx/*, ExDgnDumper/ExDgnElementDumperPE.cpp, DgProperties/DgTableProperties.cpp}`, `Dgn/Examples/{ExDgnCreate/ExDgnFiller.cpp, ExDgnCreate/ExDgnCreate.cpp, ExDgnRecomputeAssocPoints/DgRecomputeAssocPtsPEImpl.cpp, ExDgnViewCreate/ExDgnViewFiller.cpp}`, `Exchange/{Imports/DgnImport/DgnImportTables.cpp, Imports/DgnImport/DgnImportXRef.cpp, Imports/DgnImport/DgnImportGeoData.cpp, Exports/DgnExport/DgnExportImpl.cpp}`, `DgnDwg/Examples/Win/OdaDgnApp/*.cpp`; `C:\Users\david\Desktop\doc\ODA Platform Release Notes.pdf` (2027.1 p.196, 2027.3 pp.152-153). Bentley install: reflection over `Bentley.DgnPlatformNET.dll`, `Bentley.DgnGeoCoord2.dll`, `Bentley.GeoCoord2.dll`, `Bentley.GeometryNET.Structs.dll`, `ustation.dll`, `Assemblies\Bentley.Interop.MicroStationDGN.dll` (MicroStation 2026 and OpenRoads Designer 2025); `MicroStation\MicroStationVBA.chm`, `vba_concept.chm`; `C:\ProgramData\Bentley\PowerPlatformPython\Examples\Microstation\Intellisense\MSPyDgnPlatform.pyi` (ModelInfo @83388, DgnGCS @24033, BaseGCS @4241, IAuxCoordSys @56255, IACSManager @55905, DgnAttachment @16269, GeoCoordinationState @54143, LocalTransformType @71543). Scratch dumps: `C:\Users\david\AppData\Local\Temp\claude\scratch-bentley\` (`api-*.txt`, `stub-*.txt`, `chm\html\*.htm`).

**Bentley help (docs.bentley.com).** ACTIVE ORIGIN (GO=) `MicroStation%20Help-v20/en/UtilitiesGlobalOrigin.html`; To Determine the Location of the Global Origin `MicroStation%20Help-v26/en/GUID-3E13839D-D8D7-308A-7679-2C493FE29A0B.html`; GO by Monument Point `MicroStation%20PowerDraft%20Help-v15/en/GUID-C67C879F-34AE-B3EA-827C-852212B991BF.html`; GO by Center `MicroStation-v2026/Help/en/topics/123009/GUID-3F75B50D-9E6D-62E5-72FA-AD8031303E14.html`; Setting the Global Origin `MicroStation-v2025/Help/en/topics/122971/GUID-BD3AC3B4-AB98-45BF-7363-B0372113B8A8.html`; Working Units `MicroStation%20Help-v22/en/GUID-7CB44B27-7B07-AC88-5CB8-63FE53A76E02.html`, `…/DesignFileWorkingUnits.html`; Advanced Unit Settings `MicroStation%20Help-v23/en/AdvancedUnitSettings.html`; Solids Working Area `MicroStation%20PowerDraft%20Help-v16/en/GUID-311D6BFD-EC0A-4056-B7D4-85C2F47CDB2B.html`; Design cube `MicroStation-v2025.0.1/Help/en/topics/122996/GUID-A3FDC302-1554-6B6D-BB0E-79C0F2B8B6F7.html`; Angle Readout `MicroStation%20Help-v24/en/GUID-282CAE48-A872-A0B7-C457-A265C553DC08.html`; DEFINE NORTH `MicroStation%20Help-v22/en/UtilitiesDefineNorth.html`; Light Manager Solar `MicroStation%20Help-v26/en/LightManagerPropertiesSolar.html`; Grid `MicroStation%20PowerDraft%20Help-v15/en/DesignFileGrid.html`; Using ACS `MicroStation%20Help-v20/en/GUID-7A6F2241-51B6-84B4-91A5-E17CB30DDC57.html`, `MicroStation%20Help-v26/en/GUID-E37AB992-F214-965E-F59E-FB92197C989F.html`; Auxiliary Coordinates dialog `MicroStation%20PowerDraft%20Help-v14/en/AuxCoordinateSystems.html`; ACS Toolbox `MicroStation%20Help-v25/en/GUID-8B9D6161-CF1A-6E74-BB62-A9CCD906CBE2.html`; ACS plane lock `MicroStation%20Help-v23/en/GUID-5D26CE52-63DA-320D-C331-54308A7C716B.html`; Lat/Long coordinates `MicroStation%20PowerDraft%20Help-v16/en/GUID-D7EFCF8E-A27A-60E2-8CC0-2ED64A2D5086.html`; GCS overview `MicroStation%20Help-v21/en/GUID-C28D3BD9-A4AF-36AD-DCD7-2508B9D3631C.html`; GCS dialog `MicroStation%20Help-v26/en/GeographicCoordinateSystem.html`; GCS Fundamentals `prd-aws-docs.bentley.com/…/MicroStation-v2026/Help/en/topics/123004/GUID-A38504C1-5B67-4B5B-DA82-964145F9BE78.html`; Reprojecting Design Data `MicroStation%20Help-v19/en/GUID-8CDA8A17-C073-F4D9-9D7C-1B02EAA74075.html`; Local Transforms / push-pull GCS `MicroStation-v2025/Help/en/topics/123004/GUID-D08E9A5D-5A43-BC02-A18D-2F5383E1C5CD.html`; GeoReferencing `MicroStation-v2025/Help/en/topics/123004/GUID-78CED4A8-D92F-2785-B44E-AB6BA4BBBF90.html`; Define Placemark Monument `MicroStation%20Help-v19/en/GoogleEarthDefinePlacemarkMonument.html`; Export Google Earth `MicroStation%20Help-v26/en/GoogleEarthExportKMLFile.html`; Custom coordinate systems `MicroStation%20Help-v23/en/GUID-50A06DB0-354B-4A7C-A4CC-71A70063C761.html`; Attach Reference `MicroStation-v2025/Help/en/topics/270801/GUID-05888A1B-5BBF-935C-F5DB-FC43F8124C78.html`, `MicroStation%20Help-v26/en/AttachReferenceFile.html`; Attach Coincident `MicroStation%20Help-v26/en/GUID-AD613666-55FE-D9AB-878C-033673AA5A43.html`; Geographic modes `MicroStation%20Help-v22/en/GUID-5AF5573D-D9F3-6BFF-7F00-3779FBA89928.html`; References key-in format `MicroStation%20Help-v19/en/GUID-7A13CA42-5F9A-D85F-11F0-922BBD09C2D8.html`; Reference settings key-ins `MicroStation%20Help-v24/en/GUID-00D63904-3696-B65F-572E-6700411189A7.html`; Reference configuration variables `MicroStation%20PowerDraft%20Help-v15/en/GUID-012ABCFA-7C70-ED30-FEC3-A244BD9DCAFD.html`; View Rotation `microstation%20help-v20/en/GUID-CD169852-B2A5-47B0-5E5C-CAB7B48C4BA8.html`; ORD Design File Settings `OpenRoads%20Designer%20CONNECT-v12/en/GUID-5399D248-F7BD-EF40-AFF9-3F5706F32879.html`; ORD Select GCS dialog `OpenRoads%20Designer%20CONNECT-v11/en/SelectGeographicCoordinateSystemdialog.html`; ORD GCS In References / From Placemarks dialogs `OpenRoads%20Designer%20CONNECT-v12/en/GeographicCoordinateSystem{InReferences,FromPlacemarks}dialog.html`; ORD Bing Maps `OpenRoads%20Designer-v2024.1/Help/en/topics/122993/GUID-E349623C-B31E-4837-9C86-AEB08DA89896.html`; ORD SDK sample (DgnGCS.FromModel) `OpenRoads%20Designer%20SDK-v2025/Help/en/topics/2935877/GUID-257B97B7-C79D-4E6A-99BD-F034E0C46425.html`; ORD SDK Understanding DGN File `OpenRoads%20Designer%20SDK%20Help-v1/en/GUID-0DB146F7-8DDB-4899-8301-5D85155B5E04.html`; OpenRail Reprojecting `OpenRail%20Designer-v12/en/GUID-8CDA8A17-C073-F4D9-9D7C-1B02EAA74075.html`; OBD GeoReferencing `OpenBuildings%20Designer%20Help-v4/en/GUID-78CED4A8-D92F-2785-B44E-AB6BA4BBBF90.html`; OBD IFC Export `OpenBuildings%20Designer%20Help-v9/en/TriFormaIfcExportDbox.html`; ABD Global Offset Coordinates `AECOsim%20Building%20Designer%20Help-v2/en/GUID-53CED1FB-BF24-391F-0DCB-D92B9116B104.html`; MicroStation 2025/2026 what's new `MicroStation-v2026.0.1/Help/en/topics/Concept/new_and_changed_in_microstation_2025.html`, `prd-aws-docs.bentley.com/…/new_and_changed_in_microstation_2026.html`.

**Bentley Python API reference (C++ doc mirror).** `https://developer.bentley.com/documentation/microstation-python-api/apireference/MSPyDgnPlatformModule/{ModelInfo,DgnModel,DgnModelRef,DgnAttachment,IACSManager,IAuxCoordSys,DgnGCS,BaseGCS,GeoCoordinationState,IGeoCoordinateEventHandler,UnitDefinition}/`; "Structure of DGN File" PDF `https://developer.bentley.com/documentation/microstation-python-api/pdf/05-MicroStationPython_Structure_of_DGN_File.pdf` (pp. 38-39 DgnAttachment).

**Bentley KB (ServiceNow, `https://bentleysystems.service-now.com/community?id=kb_article_view&sysparm_article=<KB>`).** KB0108703 *Your Solids Working Area, Global Origin, Working Units and You*; KB0113987 *Global Origin – Coordinate Systems*; KB0039749 *Global Origins*; KB0039943; KB0110177; KB0026925; KB0026699 *Reprojection Settings*; KB0109867 *Assigning a GCS*; KB0099480 *Creating a GCS in BIM Projects*; KB0108035; KB0113615; KB0021971; KB0113993 (per-view ACS); KB0114268; KB0012597 / KB0108173 (SDK/.NET versions); KB0012896.

**Bentley Communities threads (`…/community?id=community_question&sys_id=<id>`).** `9ca153e5473d869088c56642846d4312` (DgnGCS.LatLongFromCartesian wrong; .NET GlobalOrigin/UorPerMaster/ACSManager code); `26f328b147bd0690e3378d53636d4357` (add DGN model reference in .NET); `7674112d47314e50e3378d53636d4325` (create/set custom GCS, C++); `36bc68f51bbd0690dc6db99f034bcb5d` (deleting a GCS-created ACS); `41b1e3e997f54650afb952800153afda` (Get and Set ACS, C++); `d58e56721b294a50f3fc5287624bcb56` (Design Plane Origin); `9a7c45b2476d42509091861f536d431f` (Global Origin and Design Plane); `2ed5ee0b472546509091861f536d432f` (V7/V8 GO); `282ea3fa1ba58a50f3fc5287624bcb2e` (Coincident/Coincident World); `beb451891b650a10f3fc5287624bcb45` (DWGs, GOs and coincident); `9ce7268f472546509091861f536d43cf` (attachment switched to Geographic Reprojected; MS_REF_DEFAULTSETTINGS); `81160eb41bad8610f3fc5287624bcbf3` (Revit export to DGN keeps GO offset); `6a8627a047a186109091861f536d432e` (IFC export to geographic coordinates); `2632af339792ca14afb952800153af3f` (DgnPlatformNET vs Interop); `1a595493472986509091861f536d43fa` (Help with ACS); `f7f3f4961b6dc250f3fc5287624bcb01` (ORD GO shift); `4fe919a547b5829088c56642846d43fb`, `2c77c28e1b879a147171ff7e034bcbe9` (GCS in .NET/VBA/Interop); `d1a7d7d21ba94650f3fc5287624bcbe5` (Helmert .dty); SDK blogs `0f48a72b1bf7ca58039521fcbc4bcbc9` (2024), `8d506b1e87432a105d587556cebb3590` (2025), `3da4040b1b5b7e1474968730604bcbd7` (2026); archived VBA thread `https://communities.bentley.com/products/programming/microstation_programming/f/archived-microstation-v8-2004-edition-vba-forum/46380/activemodelreference-globalorigin-returns-0-0-0`.

**iTwin / Bentley platform.** `https://www.itwinjs.org/learning/geolocation/`; `https://www.itwinjs.org/reference/core-common/imodels/imodel/`; `https://www.itwinjs.org/reference/core-common/imodels/eceflocationprops/`; `https://www.itwinjs.org/v2/learning/writeaconnector/`; `https://developer.bentley.com/apis/synchronization/overview/`.

**DOT / consultancy / third party.** WSDOT *Coincident Referencing* `https://wsdot.wa.gov/publications/fulltext/design/cae/TechNotes/MS_CoincidentRef.pdf`, *Working with GCS* `…/MS_WorkingwithGCS.pdf`; VDOT Helmert how-to `https://www.vdot.virginia.gov/media/vdotvirginiagov/doing-business/tools/openroads-and-geopak/Working_with_VDOT_Geographic_Coordinate_Systems_in_MicroStation.pdf`; OHDOT ORD Survey 200 GCS `https://tas.transportation.ohio.gov/CADD/OHDOT/Standards/OHDOT%20Utilities/Training/OpenRoads/ORD%20Survey/Guides/OHDOT_ORD_Survey_200_Geographic_Coordinate_Systems.pdf`; FHWA `https://flh.fhwa.dot.gov/resources/cadd/efl/files/cpg/Chapter_9_V8i_CPG.pdf`; IFC-SG OBD guide `https://info.corenet.gov.sg/docs/default-source/ifcsg-docs/bentley/ifc-sg-how-to-guide-openbuildings-designer.pdf`; bentleyuser.org *MicroStation 101: Global Origins* `http://www.bentleyuser.org/FeatureDetail.asp?ContentID=146`; Evolve Consultancy `https://evolve-consultancy.com/product/microstation-coordinate-space/`, `…/microstation-creating-a-project-acs/`; EnvisionCAD `https://envisioncad.com/georeferenced-reference-attachments/`; caddfix `https://caddfix.blogspot.com/2022/03/reference-attachment-methods-in_24.html`; canadacad `https://www.canadacad.ca/how-to-change-global-origin-in-microstation/`; LA Solutions `https://www.la-solutions.org/CONNECT/MVBA/MVBA-References.htm`, `…/CONNECT/DgnPlatformNet/{ArticleIndex-DgnPlatformNet,ModelEnumeration,ReferenceManager,DotNetDevelopmentEnvironment}.htm`; legacy help mirrors `http://mdlapps.com/microstation/ustnhelp{1972,1520,1518,1801,336,252,1973}.html`.

**Autodesk / interop.** Revit Positioning for Imports and Links `https://help.autodesk.com/cloudhelp/2023/ENU/Revit-Model/files/GUID-C922A152-0A30-4E27-BED2-BCE9FF2E5E30.htm`; PBP & Survey Point `…/2022/ENU/Revit-Model/files/GUID-68611F67-ED48-4659-9C3B-59C5024CE5F2.htm`; Define the PBP `…/2020/ENU/Revit-Model/files/GUID-30D76259-CC67-4498-B06B-91F7517F9B65.htm`; Rotate True North `…/2022/ENU/Revit-Model/files/GUID-CD5FEE13-B63E-4668-AC03-7D0E8A4C9698.htm`; DWG export units/coordinates `…/2023/ENU/Revit-DocumentPresent/files/GUID-DE0C0484-D7E8-4726-BAB9-9866663DB65F.htm`; About Exporting to DGN `…/2024/ENU/Revit-DocumentPresent/files/GUID-80F1834D-DC76-4615-87A2-89DEDE62A845.htm`; Autodesk idea *shared coords in DGN export* `https://forums.autodesk.com/t5/revit-ideas/add-shared-coordinates-to-dgn-export/idi-p/9047775`; Navisworks DGN reader `https://help.autodesk.com/cloudhelp/2014/ENU/Navisworks-Manage/files/GUID-C6C05410-D2E3-43B7-AEB9-E809FA866488.htm`; BIM Pure base points `https://www.bimpure.com/blog/13-tips-to-understand-revit-base-points-and-coordinate-system`; The Building Coder survey/base point `http://jeremytammik.github.io/tbc/a/0861_survey_base_pnt.htm`; `https://blog.autodesk.io/rotate-true-north/`; `https://www.learnrevitapi.com/blog/convert-coordinate-systems-in-revit-api-draft`.

**Other converters / standards.** FME DGN V8 reader `https://docs.safe.com/fme/2024.2/html/FME-Form-Documentation/FME-ReadersWriters/igds/DGNV8_reader.htm`, FME GO threads `https://community.safe.com/authoring-6/can-t-set-dgn-v8-global-origin-in-workbench-12128`, `https://community.safe.com/data-7/microstation-v8-to-microstation-v7-12014`; NVIDIA Omniverse DGN converter (`applyGlobalOrigin`) `https://docs.omniverse.nvidia.com/kit/docs/omni.kit.converter.dgn_core/511.4.3/omni.converter.dgn/omni.converter.dgn.Parameters.html`; Okino DGN import `https://www.okino.com/conv/imp_dgn.htm`; Rhino 8 DGN import `https://docs.mcneel.com/rhino/8/help/en-us/fileio/microstation_dgn_import.htm`, McNeel forum `https://discourse.mcneel.com/t/importing-coordinates-from-microstation-dgn/119253`; GDAL DGN `https://gdal.org/en/stable/drivers/vector/dgn.html`, DGNv8 `https://gdal.org/en/stable/drivers/vector/dgnv8.html`; IfcMapConversion (IFC4.3) `https://ifc43-docs.standards.buildingsmart.org/IFC/RELEASE/IFC4x3/HTML/lexical/IfcMapConversion.htm`; thinkmoult IFC CRS & Revit `https://thinkmoult.com/ifc-coordinate-reference-systems-and-revit.html`; IfcGeoRef LoGeoRef `https://raw.githubusercontent.com/dd-bim/IfcGeoRef/master/Documentation_v3.md`; ODA blogs `https://www.opendesign.com/blog/2019/august/creating-and-rendering-pdf-attachments-dgn-files`, `…/2017/july/creating-table-elements-dgn-files`, `…/2017/june/raster-reference-element-feature-dgn-files`; public ODA header mirrors used for version history `https://github.com/DF-OUTSIDER/TeighaExporter` (Teigha 19.8), `https://github.com/Jangshin/ODA-Parse-DWGs` (25.4). Note: docs.opendesign.com API reference and Bentley Communities non-accepted replies require login and could not be read.
