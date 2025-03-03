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
          List<RG.Curve> displayCurves = new();
          foreach (var rhinoCurve in ((RG.Hatch)hatchObject.Geometry).Get3dCurves(true))
          {
            displayCurves.Add(rhinoCurve);
          }

          foreach (var rhinoCurve in ((RG.Hatch)hatchObject.Geometry).Get3dCurves(false))
          {
            displayCurves.Add(rhinoCurve);
          }

          renderMeshes = new[] { RG.Mesh.CreateFromLines(displayCurves.ToArray(), 3, 0.05) };
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
