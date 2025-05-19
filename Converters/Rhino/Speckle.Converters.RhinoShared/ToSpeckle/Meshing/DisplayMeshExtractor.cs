using Rhino.DocObjects;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.Extensions;
using Speckle.DoubleNumerics;
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
  public static RG.Mesh GetGeometryDisplayMesh(RG.GeometryBase geometry, bool highPrecision = false)
  {
    double minEdgeLength = highPrecision ? GetAccurateMinEdgeLegth(geometry) : 0.05;

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
  /// Calculating optimal meshing parameter 'minimumEdgeLength' for the given geometry.
  /// </summary>
  private static double GetAccurateMinEdgeLegth(RG.GeometryBase geometry)
  {
    // adjust meshing parameters if Brep edges are too close to the document tolerance
    double minEdgeLength = 0.05;
    if (geometry is RG.Brep brep && brep.Edges.Any(x => x.GetLength() < minEdgeLength))
    {
      return 0;
    }

    return minEdgeLength;
  }

  /// <summary>
  /// Extracting Rhino Mesh and converting to Speckle with the most suitable settings (e.g. moving to origin first, if needed)
  /// This is needed because of Rhino using single precision numbers for Mesh vertices: https://wiki.mcneel.com/rhino/farfromorigin
  /// </summary>
  /// <returns>List of converted Speckle meshes</returns>
  public static List<SOG.Mesh> GetSpeckleMeshes(
    RG.GeometryBase geometry,
    bool modelFarFromOrigin,
    string units,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter
  )
  {
    RG.GeometryBase geometryToMesh = geometry;
    RG.Vector3d? vector = null;

    // 1.1. If needed, move geometry to origin
    if (modelFarFromOrigin && geometry.IsFarFromOrigin(out RG.Vector3d vectorToGeometry))
    {
      geometryToMesh = geometry.Duplicate();
      geometryToMesh.Transform(RG.Transform.Translation(-vectorToGeometry));
      vector = vectorToGeometry;
    }
    // 1.2. Extract Rhino Mesh
    RG.Mesh movedDisplayMesh = GetGeometryDisplayMesh(geometryToMesh, true);

    // 2. Convert extracted Mesh to Speckle. We don't move geometry back yet, because 'far from origin' geometry is causing Speckle conversion issues too
    List<SOG.Mesh> displayValue = new() { meshConverter.Convert(movedDisplayMesh) };

    // 3. Move Speckle geometry back from origin, if translation was applied
    MoveSpeckleMeshes(displayValue, vector, units);

    return displayValue;
  }

  public static void MoveSpeckleMeshes(List<SOG.Mesh> displayValue, RG.Vector3d? vectorToGeometry, string units)
  {
    if (vectorToGeometry is RG.Vector3d vector)
    {
      Matrix4x4 matrix = new(1, 0, 0, vector.X, 0, 1, 0, vector.Y, 0, 0, 1, vector.Z, 0, 0, 0, 1);
      SO.Transform transform = new() { matrix = matrix, units = units };
      displayValue.ForEach(x => x.Transform(transform));
    }
  }
}
