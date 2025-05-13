using Rhino;
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

  public static RG.Mesh? GetGeometryDisplayMesh(RG.GeometryBase geometry)
  {
    // declare "renderMeshes" as a separate var, because it needs to be checked for null after each Mesh.Create method
    RG.Mesh[] renderMeshes;
    var joinedMesh = new RG.Mesh();

    // if far from origin and risking faulty meshes due to precision errors: duplicate geometry and move to origin first
    (RG.GeometryBase geometryToMesh, RG.Vector3d? translationVector) = GetGeometryToMesh(geometry);
    if (translationVector is RG.Vector3d geometryCenterVector)
    {
      geometryToMesh.Transform(RG.Transform.Translation(-geometryCenterVector));
    }

    switch (geometryToMesh)
    {
      case RG.Brep brep:
        renderMeshes = RG.Mesh.CreateFromBrep(brep, new(0.05, 0.05));
        break;
      case RG.SubD subd:
#pragma warning disable CA2000
        var subdMesh = RG.Mesh.CreateFromSubD(subd, 0);
#pragma warning restore CA2000
        renderMeshes = [subdMesh];
        break;
      case RG.Extrusion extrusion:
        renderMeshes = RG.Mesh.CreateFromBrep(extrusion.ToBrep(), new(0.05, 0.05));
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
    // move geometry back, if it was relocated to origin (to prevent precision errors)
    if (translationVector is RG.Vector3d geomCenter)
    {
      joinedMesh.Transform(RG.Transform.Translation(geomCenter));
    }
    return joinedMesh;
  }

  /// <summary>
  /// Quick check whether any of the objects in the scene might be located too far from origin, to cause precision issues during meshing.
  /// </summary>
  private static bool ObjectsTooFarFromOrigin()
  {
    RG.BoundingBox bbox = RhinoDoc.ActiveDoc.Objects.BoundingBox;
    if (bbox.Min.DistanceTo(RG.Point3d.Origin) > 1e6 || bbox.Max.DistanceTo(RG.Point3d.Origin) > 1e6)
    {
      return true;
    }
    return false;
  }

  /// <summary>
  /// Returns the duplicate of geometry and its Bbox center, if the precision errors are expected, and we will need to move the geometry to origin first.
  /// </summary>
  private static (RG.GeometryBase, RG.Vector3d?) GetGeometryToMesh(RG.GeometryBase geometry)
  {
    if (ObjectsTooFarFromOrigin())
    {
      var geometryBbox = geometry.GetBoundingBox(false); // 'false' for 'accurate' parameter to accelerate bbox calculation
      return (geometry.Duplicate(), new RG.Vector3d(geometryBbox.Center)); // make all manipulations on the duplicate object
    }

    return (geometry, null);
  }
}
