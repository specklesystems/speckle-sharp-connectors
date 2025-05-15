using Rhino.DocObjects;
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
    double minEdgeLength = highPrecision ? 0 : 0.05;

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
  /// Extracting Rhino Mesh from Rhino GeometryBase after moving it to origin (if needed).
  /// </summary>
  public static RG.Mesh MoveToOriginAndGetDisplayMesh(
    RG.GeometryBase geometry,
    bool modelFarFromOrigin,
    out RG.Vector3d? vectorToOriginalGeometry
  )
  {
    vectorToOriginalGeometry = null;

    // 1. General check: if Model is NOT far from origin (99% of Rhino models): extract meshes as usual
    if (!modelFarFromOrigin)
    {
      return GetGeometryDisplayMesh(geometry, true);
    }
    // 2. Geometry check: if the model extent is far from origin, but object itself is NOT far from origin: extract meshes as usual
    if (!TryGetTranslationVector(geometry, out RG.Vector3d vectorToGeometry))
    {
      return GetGeometryDisplayMesh(geometry, true);
    }
    // 3. If the object is far from origin and risking faulty meshes due to precision errors: duplicate geometry and move it to origin
    RG.GeometryBase geometryToMesh = geometry.Duplicate();
    geometryToMesh.Transform(RG.Transform.Translation(-vectorToGeometry));

    vectorToOriginalGeometry = vectorToGeometry;
    return GetGeometryDisplayMesh(geometryToMesh, true);
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
