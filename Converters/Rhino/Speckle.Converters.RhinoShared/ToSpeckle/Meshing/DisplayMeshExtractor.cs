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
    else if (renderMeshes.Length == 0)
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
          if (subDObject.Geometry as RG.SubD is RG.SubD subdGeometry)
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

    switch (geometry)
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
}
