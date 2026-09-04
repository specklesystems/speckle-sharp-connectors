using System.Globalization;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Rhino.ToHost.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using DataObject = Speckle.Objects.Data.DataObject;
using RG = Rhino.Geometry;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Builds the Grasshopper <see cref="SpeckleCollectionWrapper"/> tree directly from a Speckle 4.0 artefact
/// <see cref="ArtefactBundle"/> — the receive-side twin of <c>GrasshopperArtifactRootObjectBuilder</c>. Unlike the
/// Rhino/Revit artefact receivers it does NOT bake into a document; it emits the same wrapper graph the GH Load
/// component outputs onto the canvas (so no <c>IHostObjectBuilder</c>). Geometry is decoded straight from the bundle
/// (meshes/point clouds/curves/points → SGEO via <see cref="SgeoDecoder"/>, raw 3dm →
/// <see cref="RawEncodingToHost.Convert3dm"/>) and reconverted to a clean Speckle <see cref="Base"/> via the shared
/// Rhino converters; the collection tree comes from the bundle's default scene view; per-object properties are
/// carried through.
/// </summary>
internal sealed class GrasshopperArtefactObjectBuilder
{
  // (root, per-fragment decode/convert warnings) — the caller (ReceiveComponent/ReceiveAsyncComponent) surfaces these
  // as GH runtime messages so an undecodable fragment is visible instead of a silent skip.
  public (SpeckleCollectionWrapper Root, IReadOnlyList<string> Warnings) Build(
    ArtefactBundle bundle,
    string rootName,
    SpeckleModelContext context
  )
  {
    var warnings = new List<string>();
    var root = new SpeckleCollectionWrapper
    {
      Base = new Collection { name = rootName },
      Name = rootName,
      Color = null,
      Material = null,
      ApplicationId = Guid.NewGuid().ToString(),
      Path = new List<string> { rootName },
    };

    var collectionCache = new Dictionary<string, SpeckleCollectionWrapper>(StringComparer.Ordinal);
    var rels = bundle.Relations;
    // GH data-tree topology (R4) per collection node K, precomputed once (mirrors CreateMaterials/CreateColors below).
    var collectionTopologies = CreateCollectionTopologies(bundle);

    // HAS_MATERIAL/HAS_COLOR bind to an object's *display-mesh* geometry K specifically — never its SOLID (3dm) K, per
    // both RhinoBundleBuilder and GrasshopperArtifactRootObjectBuilder's send-side comments. DecodeObjectGeometry
    // prefers the solid when both exist, so for a solid-backed object the geomK actually decoded never appears in
    // MaterialByGeometry/ColorByGeometry at all. Resolve via the object instead (through the Display-edges reverse map),
    // matching RhinoHostObjectArtefactBuilder.CreateMaterials/CreateColors, so material/color don't depend on which of
    // solid/display happened to be decoded. materialByGeometry still covers DEFINITION/instance content, which has no
    // owning object reachable this way (mirrors Rhino's "standalone definition geometry" byGeometry fallback).
    var objByGeom = rels.ObjectByGeometry();
    var (materialByObject, materialByGeometry, materialByInstance) = CreateMaterials(bundle, objByGeom);
    var colorByObject = CreateColors(bundle, objByGeom);

    // Instances (skip entirely if the bundle has none, mirrors RhinoHostObjectArtefactBuilder.BakeAll's gate): one
    // SpeckleBlockDefinitionWrapper per DEFINITION node, built once and shared by every placement, so a placement
    // becomes a SpeckleBlockInstanceWrapper referencing the shared definition instead of duplicated+transformed geometry.
    var definitions = new Dictionary<int, SpeckleBlockDefinitionWrapper>();
    var instEdgesByObject = new Dictionary<int, List<RelationRow>>();
    var nestedInstanceNodes = new HashSet<int>();
    if (rels.DisplayInstanceEdges.Count > 0)
    {
      definitions = BuildDefinitions(bundle, rels, materialByGeometry, warnings);
      // grouped by owning object: an object may place several instances (e.g. a railing → balusters).
      instEdgesByObject = rels.DisplayInstanceEdges.GroupBy(e => e.Src).ToDictionary(g => g.Key, g => g.ToList());
      // INSTANCE nodes that are a nested placement inside some DEFINITION (DEFINES_INSTANCE targets) — already
      // represented as a nested SpeckleBlockInstanceWrapper inside the parent definition (see BuildDefinitions), so
      // their own top-level DISPLAY_INSTANCE edge (definition-local transform) is skipped below to avoid placing the
      // nested block twice.
      nestedInstanceNodes = new HashSet<int>(rels.DefinesInstanceByDefinition.Values.SelectMany(v => v));
    }

    foreach (var kv in bundle.ObjectAppIds)
    {
      int objK = kv.Key;
      string appId = kv.Value;

      var geometries = DecodeObjectGeometry(
        objK,
        bundle,
        rels,
        ObjectUnits(bundle, objK),
        ObjectSourceType(bundle, objK),
        warnings
      );

      // An object placed as a block carries no direct SOLID/DISPLAY geometry — resolve its placement(s) to the shared
      // SpeckleBlockDefinitionWrapper built above instead. Without this, instance-only sub-models receive as empty.
      var validInstEdges = ResolveValidInstanceEdges(objK, instEdgesByObject, nestedInstanceNodes, bundle, definitions);
      int instCount = validInstEdges?.Count ?? 0;

      var props = bundle.ObjectProperties(objK);
      var name = ObjectName(props);

      if (geometries.Count == 0 && instCount == 0)
      {
        // A room, level or area carried properties and no geometry in v3, and still counts as an object [ENG-9159].
        // Only re-emit one when the source really was a DataObject - WasDataObject reads speckle_type.
        if (!WasDataObject(props, 0))
        {
          continue;
        }
      }

      var segments = SceneViewResolver.Segments(bundle, objK);
      var collection = GetOrCreateCollection(
        root,
        rootName,
        segments,
        collectionCache,
        objK,
        rels,
        collectionTopologies
      );

      int totalCount = geometries.Count + instCount;
      var (ord, geometryWrappers) = EmitGeometryWrappers(
        objK,
        geometries,
        totalCount,
        appId,
        name,
        props,
        collection,
        colorByObject,
        materialByObject,
        materialByGeometry
      );

      // Never fan one object out into several wrappers: name and properties would be copied onto each, and a sum over
      // them double-counts [ENG-9382]. An empty object is only right for a genuinely property-only source - one
      // placed as a block already comes through as its instance wrapper, and adding an empty one beside it would
      // double the count.
      if (
        (WasDataObject(props, geometryWrappers.Count) || geometryWrappers.Count > 1)
        && (geometryWrappers.Count > 0 || instCount == 0)
      )
      {
        collection.Elements.Add(BuildDataObject(objK, appId, name, props, geometryWrappers, collection));
      }
      else
      {
        foreach (var geometryWrapper in geometryWrappers)
        {
          collection.Elements.Add(geometryWrapper);
        }
      }

      if (validInstEdges is not null)
      {
        EmitInstanceWrappers(
          objK,
          validInstEdges,
          bundle,
          definitions,
          totalCount,
          ord,
          appId,
          name,
          props,
          collection,
          colorByObject,
          materialByObject,
          materialByInstance
        );
      }
    }

    // stamp last. definitions come from the map, not the tree - they hang off instances rather than Elements
    root.SetModelContext(context);
    foreach (var definition in definitions.Values)
    {
      definition.ModelContext = context;
      foreach (var member in definition.Objects)
      {
        member.ModelContext = context;
      }
    }

    return (root, warnings);
  }

  // Filters an object's DISPLAY_INSTANCE edges down to the ones this receive path can actually place: not a nested
  // placement already represented inside its parent definition (see nestedInstanceNodes), and resolving to a
  // successfully-built SpeckleBlockDefinitionWrapper.
  private static List<RelationRow>? ResolveValidInstanceEdges(
    int objK,
    Dictionary<int, List<RelationRow>> instEdgesByObject,
    HashSet<int> nestedInstanceNodes,
    ArtefactBundle bundle,
    Dictionary<int, SpeckleBlockDefinitionWrapper> definitions
  )
  {
    if (!instEdgesByObject.TryGetValue(objK, out var instEdges))
    {
      return null;
    }
    List<RelationRow>? valid = null;
    foreach (var e in instEdges)
    {
      if (nestedInstanceNodes.Contains(e.Dst))
      {
        continue; // nested placement — already represented inside its parent definition above
      }
      if (
        !bundle.Nodes.TryGetValue(e.Dst, out var instNode)
        || instNode.DefRef is not int defNodeK
        || !definitions.ContainsKey(defNodeK)
      )
      {
        continue;
      }
      (valid ??= new List<RelationRow>()).Add(e);
    }
    return valid;
  }

  // Converts and emits this object's own direct geometry as SpeckleGeometryWrappers. Returns the ordinal reached, so
  // any instance placements emitted afterward for the same object continue the same `:gN` numbering.
  private static (int Ord, List<SpeckleGeometryWrapper> Wrappers) EmitGeometryWrappers(
    int objK,
    List<(int GeomK, RG.GeometryBase Geom)> geometries,
    int totalCount,
    string appId,
    string? name,
    PropertyView props,
    SpeckleCollectionWrapper collection,
    Dictionary<string, int> colorByObject,
    Dictionary<string, SpeckleMaterialWrapper> materialByObject,
    Dictionary<int, SpeckleMaterialWrapper> materialByGeometry
  )
  {
    // returned rather than added to the collection - the caller decides whether these stand alone or get wrapped
    var created = new List<SpeckleGeometryWrapper>();
    int ord = 0;
    foreach (var (geomK, rg) in geometries)
    {
      Base? converted;
      try
      {
        converted = SpeckleConversionContext.Current.ConvertToSpeckle(rg);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        continue; // a fragment the converter can't round-trip — skip without failing the object
      }
      if (converted is null)
      {
        continue;
      }

      converted.applicationId = totalCount == 1 ? appId : $"{appId}:g{ord++}";
      var wrapper = new SpeckleGeometryWrapper
      {
        Base = converted,
        GeometryBase = rg,
        Path = collection.Path,
        Parent = collection,
        ObjectIndex = objK,
        Color = colorByObject.TryGetValue(appId, out var argb) ? System.Drawing.Color.FromArgb(argb) : null,
        Material =
          materialByObject.TryGetValue(appId, out var objMat) ? objMat
          : materialByGeometry.TryGetValue(geomK, out var geomMat) ? geomMat
          : null,
      };
      if (name is not null)
      {
        wrapper.Name = name;
      }
      if (props.Count > 0)
      {
        wrapper.Properties = new SpecklePropertyGroupGoo(props.ToNested()); // the goo renders a tree
      }
      created.Add(wrapper);
    }
    return (ord, created);
  }

  /// <summary>
  /// Whether the v3 graph would have carried this object as a DataObject. Producers write <c>speckle_type</c> as a
  /// root scalar, so this is read rather than guessed. A bundle without it falls back to the shape of the problem:
  /// more than one geometry for one object is the many-to-one case the legacy path expressed as a DataObject.
  /// </summary>
  private static bool WasDataObject(PropertyView props, int geometryCount) =>
    props.GetString("speckle_type") is { } speckleType
      ? speckleType.StartsWith("Objects.Data.", StringComparison.Ordinal)
      : geometryCount > 1;

  /// <summary>
  /// Rebuilds the DataObject the v3 graph would have had, keyed on the source object's application id so anything
  /// downstream matching on ids still lines up. The geometry lives on the wrapper, so displayValue stays empty -
  /// same as the v1 receive path does.
  /// </summary>
  private static SpeckleDataObjectWrapper BuildDataObject(
    int objK,
    string appId,
    string? name,
    PropertyView props,
    List<SpeckleGeometryWrapper> geometries,
    SpeckleCollectionWrapper collection
  )
  {
    var properties = props.ToNested(); // DataObject.properties is the v3 nested shape
    var dataObject = new DataObject
    {
      name = name ?? "",
      displayValue = [],
      properties = properties,
      applicationId = appId,
    };

    return new SpeckleDataObjectWrapper
    {
      Base = dataObject,
      Geometries = geometries,
      Path = collection.Path,
      Parent = collection,
      Name = dataObject.name,
      Properties = new SpecklePropertyGroupGoo(properties),
      ApplicationId = appId,
      ObjectIndex = objK,
    };
  }

  // Builds a SpeckleBlockInstanceWrapper per resolved DISPLAY_INSTANCE placement, referencing the shared
  // SpeckleBlockDefinitionWrapper (see BuildDefinitions) instead of duplicating its geometry.
  private static void EmitInstanceWrappers(
    int objK,
    List<RelationRow> validInstEdges,
    ArtefactBundle bundle,
    Dictionary<int, SpeckleBlockDefinitionWrapper> definitions,
    int totalCount,
    int ord,
    string appId,
    string? name,
    PropertyView props,
    SpeckleCollectionWrapper collection,
    Dictionary<string, int> colorByObject,
    Dictionary<string, SpeckleMaterialWrapper> materialByObject,
    Dictionary<int, SpeckleMaterialWrapper> materialByInstance
  )
  {
    foreach (var e in validInstEdges)
    {
      var instNode = bundle.Nodes[e.Dst];
      int defNodeK = instNode.DefRef!.Value;
      var definition = definitions[defNodeK];
      var xf = BuildTransform(
        instNode.Transform,
        instNode.Units is { Length: > 0 } u ? u : ArtefactGeometryDecoder.DocUnits()
      );

      var proxy = new InstanceProxy
      {
        definitionId = definition.ApplicationId!,
        transform = Matrix4x4.Identity,
        units = ArtefactGeometryDecoder.DocUnits(),
        maxDepth = 0,
      };
      var instanceWrapper = new SpeckleBlockInstanceWrapper
      {
        Base = proxy,
        Transform = xf,
        Definition = definition,
        GeometryBase = new RG.InstanceReferenceGeometry(Guid.Empty, xf),
        Path = collection.Path,
        Parent = collection,
        ObjectIndex = objK,
        ApplicationId = totalCount == 1 ? appId : $"{appId}:g{ord++}",
        Color = colorByObject.TryGetValue(appId, out var iArgb) ? System.Drawing.Color.FromArgb(iArgb) : null,
        // A placement's own paint arrives object-keyed (OBJECT_HAS_MATERIAL) or, in pre-split bundles, keyed by its
        // INSTANCE node K (legacy HAS_MATERIAL ord=1) — a bundle carries one vintage or the other, never both, so
        // either lookup hitting is decisive [ENG-9163, ENG-9368].
        Material =
          materialByInstance.TryGetValue(e.Dst, out var instMat) ? instMat
          : materialByObject.TryGetValue(appId, out var iMat) ? iMat
          : null,
      };
      if (name is not null)
      {
        instanceWrapper.Name = name;
      }
      if (props.Count > 0)
      {
        instanceWrapper.Properties = new SpecklePropertyGroupGoo(props.ToNested()); // the goo renders a tree
      }
      collection.Elements.Add(instanceWrapper);
    }
  }

  // Geometry indices to decode for an object: prefer the lossless SOLID (3dm) blobs, else its DISPLAY meshes. Each
  // decoded fragment keeps the geometry K it came from — for an atomic object this K is only used as the definition-
  // geometry material fallback (materials/colors on atomic objects resolve via appId instead, see Build's comment),
  // since HAS_MATERIAL/HAS_COLOR target the DISPLAY-mesh K specifically and this object may have decoded via SOLID.
  private static List<(int GeomK, RG.GeometryBase Geom)> DecodeObjectGeometry(
    int objK,
    ArtefactBundle bundle,
    ArtefactRelations rels,
    string fallbackUnits,
    string? sourceType,
    List<string> warnings
  )
  {
    var result = new List<(int, RG.GeometryBase)>();
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs))
    {
      foreach (var solidK in solidKs)
      {
        foreach (
          var g in ArtefactGeometryDecoder.DecodeGeometryIndex(solidK, bundle, fallbackUnits, sourceType, warnings)
        )
        {
          result.Add((solidK, g));
        }
      }
    }
    if (result.Count == 0 && rels.DisplayByObject(objK) is { } displayEdges)
    {
      foreach (var e in displayEdges.OrderBy(x => x.Ord))
      {
        foreach (
          var g in ArtefactGeometryDecoder.DecodeGeometryIndex(e.Dst, bundle, fallbackUnits, sourceType, warnings)
        )
        {
          result.Add((e.Dst, g));
        }
      }
    }
    return result;
  }

  /// <summary>The owning object's source type, where the SGEO primitive alone is ambiguous - see
  /// <see cref="ArtefactGeometryDecoder"/>.</summary>
  private static string? ObjectSourceType(ArtefactBundle bundle, int objK) =>
    bundle.ObjectProperties(objK).GetString("type") is { Length: > 0 } s ? s : null;

  // The send side stores "units" per object as an EAV property, falling back to the bundle's overall units when absent
  // (mirrors RhinoHostObjectArtefactBuilder.ObjectUnits).
  private static string ObjectUnits(ArtefactBundle bundle, int objK) =>
    bundle.ObjectProperties(objK).GetString("units") is { Length: > 0 } s ? s : bundle.Units;

  // The send side stores "name" as (Attributes.Name || sourceType) alongside the "type" scalar (== sourceType), so an
  // unnamed object has name == type. Returns the real name only when it's present and differs from type; null otherwise
  // (missing, empty, or the sourceType fallback) so unnamed objects stay unnamed on receive (mirrors
  // RhinoHostObjectArtefactBuilder.ObjectName).
  private static string? ObjectName(PropertyView props) =>
    props.GetString("name") is { Length: > 0 } name
    && !string.Equals(name, props.GetString("type"), StringComparison.Ordinal)
      ? name
      : null;

  // Builds a SpeckleMaterialWrapper per MATERIAL node, then resolves HAS_MATERIAL (Relations.MaterialByGeometry:
  // display-mesh geometry K → material node K) to BOTH a geometry-K lookup (covers standalone DEFINITION/instance
  // geometry, which has no owning object) and an object-appId lookup (covers atomic display objects, resolved via the
  // Display-edges reverse map — robust to whether the object's decoded geometry actually came from its SOLID or
  // DISPLAY blob, since HAS_MATERIAL only ever targets the display-mesh K). Placement paint arrives separately, on
  // OBJECT_HAS_MATERIAL (object plane) or the legacy INSTANCE-keyed ord=1 shape — see below. Mirrors
  // RhinoHostObjectArtefactBuilder.CreateMaterials exactly; reuses the same SpeckleMaterialWrapperGoo.CastFrom(RenderMaterial)
  // construction path the v1 GH receive uses (GrasshopperMaterialUnpacker), so a bake/re-send round-trips the same way.
  private static (
    Dictionary<string, SpeckleMaterialWrapper> ByObject,
    Dictionary<int, SpeckleMaterialWrapper> ByGeometry,
    Dictionary<int, SpeckleMaterialWrapper> ByInstance
  ) CreateMaterials(ArtefactBundle bundle, Dictionary<int, int> objByGeom)
  {
    var wrapperByMaterialNode = new Dictionary<int, SpeckleMaterialWrapper>();
    foreach (var kv in bundle.Nodes)
    {
      var n = kv.Value;
      if (n.Kind != NodeKind.Material)
      {
        continue;
      }
      try
      {
        var speckleMaterial = new SpeckleRenderMaterial
        {
          name = n.Name ?? "material",
          diffuse = n.Argb ?? unchecked((int)0xFFFFFFFF),
          opacity = n.Opacity ?? 1.0,
          metalness = n.Metalness ?? 0.0,
          roughness = n.Roughness ?? 1.0,
          applicationId = $"material-{kv.Key}",
        };
        // ENG-8791: remaining PBR channels; ior as the v1 dynamic prop so SpeckleMaterialWrapperGoo maps it
        // onto PhysicallyBasedMaterial.IndexOfRefraction (`mat["ior"]` cast in CastFrom).
        if (n.Emissive is int emissive)
        {
          speckleMaterial.emissive = emissive;
        }
        if (n.Ior is double ior)
        {
          speckleMaterial["ior"] = ior;
        }
        var goo = new SpeckleMaterialWrapperGoo();
        goo.CastFrom(speckleMaterial);
        wrapperByMaterialNode[kv.Key] = goo.Value;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // a malformed material node shouldn't fail the whole receive — the bound geometry just stays unmaterialed.
      }
    }

    var byObject = new Dictionary<string, SpeckleMaterialWrapper>();
    var byGeometry = new Dictionary<int, SpeckleMaterialWrapper>();
    foreach (var kv in bundle.Relations.MaterialByGeometry)
    {
      if (!wrapperByMaterialNode.TryGetValue(kv.Value, out var wrapper))
      {
        continue;
      }
      byGeometry[kv.Key] = wrapper; // geometry → material (covers standalone definition geometry)
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
      {
        byObject[appId] = wrapper; // object → material (atomic display objects)
      }
    }

    // Placement paint (OBJECT_HAS_MATERIAL, rel 26): a material set directly on a block placement. A placement owns
    // no geometry, so it's invisible to the geometry loop above — resolve it straight through the object dictionary,
    // the same appId key the placement's own wrapper joins on [ENG-9368]. FILL semantics (rel 26): a geometry-level
    // HAS_MATERIAL already recorded above wins, so this only fills where that said nothing.
    foreach (var kv in bundle.Relations.MaterialByObject)
    {
      if (
        wrapperByMaterialNode.TryGetValue(kv.Value, out var wrapper)
        && bundle.ObjectAppIds.TryGetValue(kv.Key, out var appId)
        && !byObject.ContainsKey(appId)
      )
      {
        byObject[appId] = wrapper;
      }
    }

    // Legacy fallback: pre-split bundles carried placement paint as HAS_MATERIAL ord=1 keyed by the INSTANCE node K
    // [ENG-9163]. Kept for bundles published before rel 26 existed. Mirrors RhinoHostObjectArtefactBuilder.
    var byInstance = new Dictionary<int, SpeckleMaterialWrapper>();
    foreach (var kv in bundle.Relations.MaterialByInstance)
    {
      if (wrapperByMaterialNode.TryGetValue(kv.Value, out var wrapper))
      {
        byInstance[kv.Key] = wrapper;
      }
    }
    return (byObject, byGeometry, byInstance);
  }

  // Resolves HAS_COLOR (Relations.ColorByGeometry: display-mesh geometry K → COLOR node) to the owning object's
  // appId → argb — the object's by-object display colour, distinct from a render material. Resolved via the same
  // Display-edges reverse map as materials, for the same reason (HAS_COLOR only ever targets the display-mesh K, not
  // a SOLID blob's). Mirrors RhinoHostObjectArtefactBuilder.CreateColors.
  private static Dictionary<string, int> CreateColors(ArtefactBundle bundle, Dictionary<int, int> objByGeom)
  {
    var byObject = new Dictionary<string, int>();
    foreach (var kv in bundle.Relations.ColorByGeometry)
    {
      if (!bundle.Nodes.TryGetValue(kv.Value, out var n) || n.Kind != NodeKind.Color || n.Argb is not int argb)
      {
        continue;
      }
      if (objByGeom.TryGetValue(kv.Key, out int objK) && bundle.ObjectAppIds.TryGetValue(objK, out var appId))
      {
        byObject[appId] = argb;
      }
    }

    // Object-sourced: a block placement's own colour. A placement owns no geometry, so it never appears in objByGeom -
    // resolve it straight through the object dictionary [ENG-9163]. Mirrors RhinoHostObjectArtefactBuilder.
    foreach (var kv in bundle.Relations.ColorByObject)
    {
      if (
        bundle.Nodes.TryGetValue(kv.Value, out var n)
        && n.Kind == NodeKind.Color
        && n.Argb is int argb
        && bundle.ObjectAppIds.TryGetValue(kv.Key, out var appId)
      )
      {
        byObject[appId] = argb;
      }
    }
    return byObject;
  }

  // Builds one SpeckleBlockDefinitionWrapper per DEFINITION node, keyed by node index: direct DEFINES → converted
  // geometry members, DEFINES_INSTANCE → nested SpeckleBlockInstanceWrapper members (mirrors
  // RhinoHostObjectArtefactBuilder.BuildDefinitions' nested composition, but keeps nested blocks as real nested
  // instances instead of baked geometry, matching GrasshopperBlockUnpacker's wrapper shape). Built once per bundle and
  // shared by reference from every placement's SpeckleBlockInstanceWrapper.Definition.
  // A definition member is still interned as an object, but never gets IN_COLLECTION/DISPLAY/SOLID edges (send side:
  // EmitGeometryObject isDefinitionMember: true), so Build()'s main loop naturally skips it standalone — no separate
  // "consumed object" tracking (cf. GrasshopperBlockUnpacker.consumedObjectIds) needed here.
  private static Dictionary<int, SpeckleBlockDefinitionWrapper> BuildDefinitions(
    ArtefactBundle bundle,
    ArtefactRelations rels,
    Dictionary<int, SpeckleMaterialWrapper> materialByGeometry,
    List<string> warnings
  )
  {
    var map = new Dictionary<int, SpeckleBlockDefinitionWrapper>();
    var building = new HashSet<int>();

    SpeckleBlockDefinitionWrapper? BuildDefinition(int defNodeK)
    {
      if (map.TryGetValue(defNodeK, out var already))
      {
        return already; // already built (a shared nested definition is reached from several parents)
      }
      if (!bundle.Nodes.TryGetValue(defNodeK, out var defNode))
      {
        return null; // dangling DefRef in a malformed bundle
      }
      if (!building.Add(defNodeK))
      {
        return null; // cycle guard — never stack-overflow on a bad bundle
      }

      var members = new List<SpeckleGeometryWrapper>();
      string defAppId = $"def-{defNodeK}";

      // direct geometry members (DEFINES → geometry blob).
      if (rels.DefinesByDefinition.TryGetValue(defNodeK, out var geomKs))
      {
        rels.DefinesOrdByDefinition.TryGetValue(defNodeK, out var ords);
        int memberOrd = 0;
        // A member's geometry shares a member ordinal; within each member prefer the authoritative 3dm solid over its
        // display mesh(es) so a solid inside a block rebuilds as a solid (Grasshopper decodes Rhino 3dm, so without
        // this it would place both the solid and its shadow mesh).
        foreach (var memberGeomKs in GroupDefinesByMember(geomKs, ords, bundle))
        {
          foreach (var geomK in memberGeomKs)
          {
            // Definition geometry is in the model's source units; pass the bundle (source) units as the fallback, NOT
            // DocUnits — otherwise a 3dm member isn't rescaled and a block sent from a metre model lands 1000x too
            // small in a millimetre GH doc (mirrors RhinoHostObjectArtefactBuilder.BuildDefinitions).
            // no owning object row for a definition member, so no source type to disambiguate points with
            foreach (var g in ArtefactGeometryDecoder.DecodeGeometryIndex(geomK, bundle, bundle.Units, null, warnings))
            {
              Base? converted;
              try
              {
                converted = SpeckleConversionContext.Current.ConvertToSpeckle(g);
              }
              catch (Exception ex) when (!ex.IsFatal())
              {
                continue; // a fragment the converter can't round-trip — skip without failing the definition
              }
              if (converted is null)
              {
                continue;
              }
              converted.applicationId = $"{defAppId}:m{memberOrd++}";
              members.Add(
                new SpeckleGeometryWrapper
                {
                  Base = converted,
                  GeometryBase = g,
                  Material = materialByGeometry.TryGetValue(geomK, out var geomMat) ? geomMat : null,
                }
              );
            }
          }
        }
      }

      // nested block members (DEFINES_INSTANCE → INSTANCE node): build the child definition first (depth-first,
      // memoized above), then wrap it as a nested SpeckleBlockInstanceWrapper carrying its own definition-local
      // transform — so a placement of THIS definition composes the nested block via SpeckleBlockInstanceWrapper's own
      // transform-combining logic (GetTransformedObjectsForDisplay) instead of pre-baked/duplicated geometry.
      if (rels.DefinesInstanceByDefinition.TryGetValue(defNodeK, out var nestedInstNodeKs))
      {
        foreach (var instNodeK in nestedInstNodeKs)
        {
          if (!bundle.Nodes.TryGetValue(instNodeK, out var nestedInst) || nestedInst.DefRef is not int childDefNodeK)
          {
            continue;
          }
          var childDef = BuildDefinition(childDefNodeK);
          if (childDef is null)
          {
            continue;
          }
          var xf = BuildTransform(nestedInst.Transform, nestedInst.Units is { Length: > 0 } u ? u : bundle.Units);
          string nestedAppId = $"{defAppId}:i{instNodeK}";
          var nestedProxy = new InstanceProxy
          {
            definitionId = childDef.ApplicationId!,
            transform = Matrix4x4.Identity,
            units = ArtefactGeometryDecoder.DocUnits(),
            maxDepth = 0,
          };
          members.Add(
            new SpeckleBlockInstanceWrapper
            {
              Base = nestedProxy,
              Transform = xf,
              Definition = childDef,
              GeometryBase = new RG.InstanceReferenceGeometry(Guid.Empty, xf),
              ApplicationId = nestedAppId,
            }
          );
        }
      }

      building.Remove(defNodeK);
      if (members.Count == 0)
      {
        return null;
      }

      var defProxy = new InstanceDefinitionProxy
      {
        objects = members.Select(m => m.ApplicationId!).ToList(),
        maxDepth = 0,
        name = defNode.Name ?? defAppId,
      };
      var definition = new SpeckleBlockDefinitionWrapper
      {
        Base = defProxy,
        Name = defProxy.name,
        ApplicationId = defAppId,
        Objects = members,
      };
      map[defNodeK] = definition;
      return definition;
    }

    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Definition)
      {
        BuildDefinition(kv.Key);
      }
    }

    return map;
  }

  // Groups a definition's DEFINES geometry Ks by member ordinal (index-aligned with ords), then within each member
  // prefers the authoritative 3dm solid over its display mesh(es); a member with no solid yields all its geometry.
  // Member order is preserved. When ords are absent (older bundle) each geometry is its own member — i.e. no grouping.
  private static IEnumerable<List<int>> GroupDefinesByMember(List<int> geomKs, List<int>? ords, ArtefactBundle bundle)
  {
    var members = new List<List<int>>();
    var indexByOrd = new Dictionary<int, int>();
    for (int i = 0; i < geomKs.Count; i++)
    {
      int ord = ords is not null && i < ords.Count ? ords[i] : -(i + 1);
      if (!indexByOrd.TryGetValue(ord, out int idx))
      {
        idx = members.Count;
        indexByOrd[ord] = idx;
        members.Add(new List<int>());
      }
      members[idx].Add(geomKs[i]);
    }
    foreach (var geoms in members)
    {
      var solids = geoms
        .Where(k => bundle.Geometries.TryGetValue(k, out var g) && g.Type == RawEncodingFormats.RHINO_3DM)
        .ToList();
      yield return solids.Count > 0 ? solids : geoms;
    }
  }

  // Parses an instance node's transform (a 16-value row-major CSV of the 4x4 matrix) into a Rhino transform, scaling
  // the translation from the instance's own units to DocUnits (mirrors RhinoHostObjectArtefactBuilder.BuildTransform;
  // rotation/scale entries are unitless ratios and need no conversion).
  private static RG.Transform BuildTransform(string? csv, string units)
  {
    var d = new double[16];
    if (csv is { Length: > 0 } text)
    {
      var parts = text.Split(',');
      for (int i = 0; i < 16 && i < parts.Length; i++)
      {
        double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i]);
      }
    }
    else
    {
      d[0] = d[5] = d[10] = d[15] = 1.0;
    }

    double scale = Units.GetConversionFactor(units, ArtefactGeometryDecoder.DocUnits());
    var t = RG.Transform.Identity;
    t.M00 = d[0];
    t.M01 = d[1];
    t.M02 = d[2];
    t.M03 = d[3] * scale;
    t.M10 = d[4];
    t.M11 = d[5];
    t.M12 = d[6];
    t.M13 = d[7] * scale;
    t.M20 = d[8];
    t.M21 = d[9];
    t.M22 = d[10];
    t.M23 = d[11] * scale;
    t.M30 = d[12];
    t.M31 = d[13];
    t.M32 = d[14];
    t.M33 = d[15];
    return t;
  }

  // Resolves (and creates once) the nested collection-wrapper chain for the given scene-view segments under the root.
  // Topology (R4) is set only on the LAST segment (v1 only ever set it on the leaf, per CollectionsByName.cs) and
  // only on first creation, so the lookup runs once per collection, not once per object.
  private static SpeckleCollectionWrapper GetOrCreateCollection(
    SpeckleCollectionWrapper root,
    string rootName,
    IReadOnlyList<string> segments,
    Dictionary<string, SpeckleCollectionWrapper> cache,
    int objK,
    ArtefactRelations rels,
    Dictionary<int, string> collectionTopologies
  )
  {
    var previous = root;
    var current = new List<string> { rootName };
    for (int i = 0; i < segments.Count; i++)
    {
      var raw = segments[i];
      var name = string.IsNullOrWhiteSpace(raw) ? "unnamed" : raw;
      current.Add(name);
      var key = string.Concat(current);
      if (cache.TryGetValue(key, out var existing))
      {
        previous = existing;
        continue;
      }

      var wrapper = new SpeckleCollectionWrapper
      {
        Base = new Collection { name = name },
        Name = name,
        ApplicationId = key,
        Path = new List<string>(current),
        Color = null,
        Material = null,
      };
      if (
        i == segments.Count - 1
        && rels.CollectionByObject.TryGetValue(objK, out int leafNodeK)
        && collectionTopologies.TryGetValue(leafNodeK, out var topology)
      )
      {
        wrapper.Topology = topology;
      }
      cache[key] = wrapper;
      previous.Elements.Add(wrapper);
      previous = wrapper;
    }
    return previous;
  }

  // Each CONTAINER node's topology (R4), from nodes.gh_topology.
  private static Dictionary<int, string> CreateCollectionTopologies(ArtefactBundle bundle)
  {
    var result = new Dictionary<int, string>();
    foreach (var kv in bundle.Nodes)
    {
      if (kv.Value.Kind == NodeKind.Container && kv.Value.GhTopology is { Length: > 0 } topology)
      {
        result[kv.Key] = topology;
      }
    }
    return result;
  }
}
