using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Rhino.ToHost.Helpers;
using Speckle.Objects.Other;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

/// <summary>
/// Builds the Grasshopper <see cref="SpeckleCollectionWrapper"/> tree directly from a Speckle 4.0 artefact
/// <see cref="ArtefactBundle"/> — the receive-side twin of <c>GrasshopperArtifactRootObjectBuilder</c>. Unlike the
/// Rhino/Revit artefact receivers it does NOT bake into a document; it emits the same wrapper graph the GH Load
/// component outputs onto the canvas (so no <c>IHostObjectBuilder</c>). Geometry is decoded straight from the bundle
/// (SGEO → <see cref="RG.Mesh"/>, raw 3dm → <see cref="RawEncodingToHost.Convert3dm"/>) and reconverted to a clean
/// Speckle <see cref="Base"/> via the shared Rhino converters; the collection tree comes from the bundle's default
/// scene view; per-object properties are carried through.
/// </summary>
/// <remarks>
/// First cut: <b>geometry + collections + properties</b>. Materials (MATERIAL/HAS_MATERIAL) and instances
/// (DEFINES/DISPLAY_INSTANCE) are NOT reconstructed yet — a fast follow. Non-geometric objects (instance placements /
/// rooms) are skipped silently.
/// </remarks>
internal sealed class GrasshopperArtefactObjectBuilder
{
  public SpeckleCollectionWrapper Build(ArtefactBundle bundle, string rootName)
  {
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

    foreach (var kv in bundle.ObjectAppIds)
    {
      int objK = kv.Key;
      string appId = kv.Value;

      var geometries = DecodeObjectGeometry(objK, bundle, rels);
      if (geometries.Count == 0)
      {
        continue; // instance placement / non-geometric element — not handled in this cut
      }

      bundle.Properties.TryGetValue(objK, out var props);
      var segments = SceneViewResolver.Segments(bundle, objK);
      var collection = GetOrCreateCollection(root, rootName, segments, collectionCache);

      int ord = 0;
      foreach (var rg in geometries)
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

        converted.applicationId = geometries.Count == 1 ? appId : $"{appId}:g{ord++}";
        var wrapper = new SpeckleGeometryWrapper
        {
          Base = converted,
          GeometryBase = rg,
          Path = collection.Path,
          Parent = collection,
          Color = null,
          Material = null,
        };
        if (props is { Count: > 0 })
        {
          wrapper.Properties = new SpecklePropertyGroupGoo(props);
        }
        collection.Elements.Add(wrapper);
      }
    }

    return root;
  }

  // Geometry indices to decode for an object: prefer the lossless SOLID (3dm) blobs, else its DISPLAY meshes.
  private static List<RG.GeometryBase> DecodeObjectGeometry(int objK, ArtefactBundle bundle, ArtefactRelations rels)
  {
    var result = new List<RG.GeometryBase>();
    if (rels.SolidByObject.TryGetValue(objK, out var solidKs))
    {
      foreach (var solidK in solidKs)
      {
        result.AddRange(DecodeGeometryIndex(solidK, bundle));
      }
    }
    if (result.Count == 0 && rels.DisplayByObject(objK) is { } displayEdges)
    {
      foreach (var e in displayEdges.OrderBy(x => x.Ord))
      {
        result.AddRange(DecodeGeometryIndex(e.Dst, bundle));
      }
    }
    return result;
  }

  private static List<RG.GeometryBase> DecodeGeometryIndex(int geomK, ArtefactBundle bundle)
  {
    if (!bundle.Geometries.TryGetValue(geomK, out var g))
    {
      return new List<RG.GeometryBase>();
    }
    if (g.Type == RawEncodingFormats.RHINO_3DM)
    {
      return RawEncodingToHost.Convert3dm(g.Content);
    }
    if (g.IsSgeo && SgeoDecoder.TryDecodeMesh(g.Content, out var sm))
    {
      return new List<RG.GeometryBase> { BuildMesh(sm) };
    }
    return new List<RG.GeometryBase>();
  }

  // SGEO neutral mesh → Rhino mesh (Speckle count-prefixed face format; mirrors RhinoHostObjectArtefactBuilder).
  private static RG.Mesh BuildMesh(SgeoMesh sm)
  {
    var mesh = new RG.Mesh();
    var v = sm.Vertices;
    for (int i = 0; i + 2 < v.Length; i += 3)
    {
      mesh.Vertices.Add(v[i], v[i + 1], v[i + 2]);
    }

    var f = sm.Faces;
    int p = 0;
    while (p < f.Length)
    {
      int n = f[p];
      if (n < 3)
      {
        n += 3; // legacy 0 -> triangle, 1 -> quad
      }
      if (n == 3 && p + 3 < f.Length)
      {
        mesh.Faces.AddFace(f[p + 1], f[p + 2], f[p + 3]);
      }
      else if (n == 4 && p + 4 < f.Length)
      {
        mesh.Faces.AddFace(f[p + 1], f[p + 2], f[p + 3], f[p + 4]);
      }
      else if (n > 4 && p + n < f.Length)
      {
        for (int k = 1; k < n - 1; k++)
        {
          mesh.Faces.AddFace(f[p + 1], f[p + 1 + k], f[p + 2 + k]);
        }
      }
      else
      {
        break;
      }
      p += n + 1;
    }

    if (sm.Colors.Length == mesh.Vertices.Count && sm.Colors.Length > 0)
    {
      foreach (var argb in sm.Colors)
      {
        mesh.VertexColors.Add(System.Drawing.Color.FromArgb(argb));
      }
    }
    mesh.Normals.ComputeNormals();
    mesh.Compact();
    return mesh;
  }

  // Resolves (and creates once) the nested collection-wrapper chain for the given scene-view segments under the root.
  private static SpeckleCollectionWrapper GetOrCreateCollection(
    SpeckleCollectionWrapper root,
    string rootName,
    IReadOnlyList<string> segments,
    Dictionary<string, SpeckleCollectionWrapper> cache
  )
  {
    var previous = root;
    var current = new List<string> { rootName };
    foreach (var raw in segments)
    {
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
      cache[key] = wrapper;
      previous.Elements.Add(wrapper);
      previous = wrapper;
    }
    return previous;
  }
}
