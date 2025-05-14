using Rhino.DocObjects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Meshing;

public static class DisplayMeshExtractor
{
  public static RG.Mesh GetDisplayMesh(RhinoObject obj)
  {
    // note: unsure this is nice, we get bigger meshes - we should to benchmark (conversion time vs size tradeoffs)
    var joinedMesh = new RG.Mesh();
    var renderMeshes = obj.GetMeshes(RG.MeshType.Render);

    if (renderMeshes.Length > 0)
    {
      joinedMesh.Append(renderMeshes);
    }
    else
    {
      switch (obj)
      {
        case BrepObject brep:
          joinedMesh.Append(GetGeometryDisplayMesh(brep.BrepGeometry));
          break;
        case ExtrusionObject extrusion:
          joinedMesh.Append(GetGeometryDisplayMesh(extrusion.ExtrusionGeometry.ToBrep()));
          break;
        case SubDObject subDObject:
          if (subDObject.Geometry is RG.SubD subdGeometry)
          {
            joinedMesh.Append(GetGeometryDisplayMesh(subdGeometry));
          }
          else
          {
            throw new ConversionException($"Failed to extract geometry from {subDObject.GetType()}");
          }
          break;
        default:
          throw new ConversionException($"Unsupported object for display mesh generation {obj.GetType().FullName}");
      }
    }

    return joinedMesh;
  }

  /// <summary>
  /// Extracting Rhino Mesh from Rhino GeometryBase using specified MeshingParameters settings, e.g. minimumEdgeLength.
  /// </summary>
  public static RG.Mesh GetGeometryDisplayMesh(RG.GeometryBase geometry, double minEdgeLength = 0.05)
  {
    // declare "renderMeshes" as a separate var, because it needs to be checked for null after each Mesh.Create method
    RG.Mesh[] renderMeshes;
    var joinedMesh = new RG.Mesh();

    switch (geometry)
    {
      case RG.Brep brep:
        renderMeshes = RG.Mesh.CreateFromBrep(brep, new(0.05, minEdgeLength));
        break;
      case RG.SubD subd:
#pragma warning disable CA2000
        var subdMesh = RG.Mesh.CreateFromSubD(subd, 0);
#pragma warning restore CA2000
        renderMeshes = [subdMesh];
        break;
      case RG.Extrusion extrusion:
        renderMeshes = RG.Mesh.CreateFromBrep(extrusion.ToBrep(), new(0.05, minEdgeLength));
        break;
      default:
        throw new ConversionException($"Unsupported object for display mesh generation {geometry.GetType().FullName}");
    }

    if (renderMeshes == null)
    {
      // MeshingParametrs with small minimumEdgeLength often leads to `CreateFromBrep` returning null
      throw new ConversionException($"Failed to meshify {geometry.GetType()} (perhaps the brep is too small?)");
    }

    joinedMesh.Append(renderMeshes);
    return joinedMesh;
  }

  /// <summary>
  /// Extracting Rhino Mesh from Rhino GeometryBase with high accuracy: taking into account distance from origin and meshing parameters vs. geometry topology.
  /// </summary>
  /// <returns>
  /// Returns the mesh of the input geometry, possibly moved to the origin for better accuracy (if returned Vector is not null).
  /// </returns>
  public static (RG.Mesh, RG.Vector3d?) GetGeometryDisplayMeshAccurate(
    RG.GeometryBase geometry,
    bool modelFarFromOrigin
  )
  {
    // adjust meshing parameters if Brep edges are too close to the document tolerance
    double minEdgeLength = 0.05;
    if (geometry is RG.Brep brep && brep.Edges.Any(x => x.GetLength() < minEdgeLength))
    {
      minEdgeLength = 0;
    }

    // preserve original behavior, if Model is not far from origin: will be the case for 99% of Rhino models
    if (!modelFarFromOrigin)
    {
      return (GetGeometryDisplayMesh(geometry, minEdgeLength), null);
    }

    // preserve original behavior if the object is not far from origin
    if (!TryGetTranslationVector(geometry, out RG.Vector3d vectorToGeometry))
    {
      return (GetGeometryDisplayMesh(geometry, minEdgeLength), null);
    }

    // if the object is far from origin and risking faulty meshes due to precision errors: then duplicate geometry and move to origin first
    RG.GeometryBase geometryToMesh = geometry.Duplicate();
    geometryToMesh.Transform(RG.Transform.Translation(-vectorToGeometry));
    return (GetGeometryDisplayMesh(geometryToMesh, minEdgeLength), vectorToGeometry);
  }

  /// <summary>
  /// Getting translation vector from origin to the Geometry bbox Center (if geometry is far from origin and translation needed)
  /// </summary>
  /// <returns>
  /// True and the vector from origin to Geometry bbox center (if translation needed), otherwise false and zero-length vector.
  /// </returns>
  private static bool TryGetTranslationVector(RG.GeometryBase geom, out RG.Vector3d vector)
  {
    vector = new RG.Vector3d();
    var geometryBbox = geom.GetBoundingBox(false); // 'false' for 'accurate' parameter to accelerate bbox calculation
    if (geometryBbox.Min.DistanceTo(RG.Point3d.Origin) > 1e6 || geometryBbox.Max.DistanceTo(RG.Point3d.Origin) > 1e6)
    {
      vector = new RG.Vector3d(geometryBbox.Center);
      return true;
    }

    return false;
  }
}
