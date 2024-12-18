using Rhino.DocObjects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Meshing;

public static class DisplayMeshExtractor
{
  public static RG.Mesh GetDisplayMesh(RhinoObject obj)
  {
    // note: unsure this is nice, we get bigger meshes - we should to benchmark (conversion time vs size tradeoffs)
    var renderMeshes = obj.GetMeshes(RG.MeshType.Render);

    if (renderMeshes.Length != 0)
    {
      var joinedMesh = new RG.Mesh();
      joinedMesh.Append(renderMeshes);
      return joinedMesh;
    }

    return GetDisplayMeshFromGeometry(obj.Geometry);
  }

  public static RG.Mesh GetDisplayMeshFromGeometry(RG.GeometryBase gb)
  {
    RG.Mesh[] renderMeshes;
    switch (gb)
    {
      case RG.Brep brep:
        renderMeshes = RG.Mesh.CreateFromBrep(brep, new(0.05, 0.05));
        break;
      case RG.Extrusion extrusion:
        renderMeshes = RG.Mesh.CreateFromBrep(extrusion.ToBrep(), new(0.05, 0.05));
        break;
      case RG.SubD subDObject:
#pragma warning disable CA2000
        var mesh = RG.Mesh.CreateFromSubD(subDObject, 0);
#pragma warning restore CA2000
        renderMeshes = [mesh];
        break;
      default:
        throw new ConversionException($"Unsupported object for display mesh generation {gb.GetType().FullName}");
    }
    var joinedMesh = new RG.Mesh();
    joinedMesh.Append(renderMeshes);
    return joinedMesh;
  }
}
