using Rhino.DocObjects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Meshing;

public static class DisplayMeshExtractor
{
  public static RG.Mesh GetDisplayMesh(RhinoObject obj)
  {
    // note: unsure this is nice, we get bigger meshes - we should to benchmark (conversion time vs size tradeoffs)
    var renderMeshes = obj.GetMeshes(RG.MeshType.Render);
    if (renderMeshes.Length == 0)
    {
      switch (obj)
      {
        case BrepObject brep:
          renderMeshes = RG.Mesh.CreateFromBrep(brep.BrepGeometry, new(0.05, 0.05));
          break;
        case ExtrusionObject extrusion:
          renderMeshes = RG.Mesh.CreateFromBrep(extrusion.ExtrusionGeometry.ToBrep(), new(0.05, 0.05));
          break;
        case HatchObject hatchObject:
          // get boundary and inner curves: already done in converter, might find a way to pass them here
          List<RG.Curve> allCurves = new() { ((RG.Hatch)hatchObject.Geometry).Get3dCurves(true)[0] };
          allCurves.AddRange(((RG.Hatch)hatchObject.Geometry).Get3dCurves(false));

          // construct a planar Brep, so Rhino native API can create a mesh from it
          var planarBreps = RG.Brep.CreatePlanarBreps(allCurves, 0.05);
          renderMeshes = planarBreps.SelectMany(x => RG.Mesh.CreateFromBrep(x, new(0.05, 0.05))).ToArray();
          break;
        case SubDObject subDObject:
#pragma warning disable CA2000
          var mesh = RG.Mesh.CreateFromSubD(subDObject.Geometry as RG.SubD, 0);
#pragma warning restore CA2000
          renderMeshes = [mesh];
          break;
        default:
          throw new ConversionException($"Unsupported object for display mesh generation {obj.GetType().FullName}");
      }
    }

    var joinedMesh = new RG.Mesh();
    joinedMesh.Append(renderMeshes);
    return joinedMesh;
  }
}
