using Rhino;
using Rhino.DocObjects;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Meshing;

public static class DisplayMeshExtractor
{
  public static RG.Mesh GetDisplayMesh(RhinoObject obj, RhinoDoc doc)
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
          joinedMesh.Append(GetGeometryDisplayMesh(brep.BrepGeometry, doc));
          break;
        case ExtrusionObject extrusion:
          joinedMesh.Append(GetGeometryDisplayMesh(extrusion.ExtrusionGeometry.ToBrep(), doc));
          break;
        case SubDObject subDObject:
          if (subDObject.Geometry is RG.SubD subdGeometry)
          {
            joinedMesh.Append(GetGeometryDisplayMesh(subdGeometry, doc));
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
  public static RG.Mesh GetGeometryDisplayMesh(RG.GeometryBase geometry, RhinoDoc doc)
  {
    // declare "renderMeshes" as a separate var, because it needs to be checked for null after each Mesh.Create method
    RG.Mesh[] renderMeshes;
    var joinedMesh = new RG.Mesh();
    RG.MeshingParameters meshParams = RG.MeshingParameters.DocumentCurrentSetting(doc);
    switch (geometry)
    {
      case RG.Brep brep:
        renderMeshes = RG.Mesh.CreateFromBrep(brep, meshParams);
        break;
      case RG.SubD subd:
#pragma warning disable CA2000
        var subdMesh = RG.Mesh.CreateFromSubD(subd, 0);
#pragma warning restore CA2000
        renderMeshes = [subdMesh];
        break;
      case RG.Extrusion extrusion:
        renderMeshes = RG.Mesh.CreateFromBrep(extrusion.ToBrep(), meshParams);
        break;
      default:
        throw new ConversionException($"Unsupported object for display mesh generation {geometry.GetType().FullName}");
    }

    if (renderMeshes == null)
    {
      // MeshingParametrs with small minimumEdgeLength often leads to `CreateFromBrep` returning null
      throw new ConversionException($"Failed to meshify {geometry.GetType()} (perhaps the brep is too small?)");
    }

    // triangulate these resulting meshes as they may contain quads
    // this saves a lot of computing time for our viewer
    foreach (var mesh in renderMeshes)
    {
      mesh.Faces.ConvertQuadsToTriangles();
    }

    joinedMesh.Append(renderMeshes);
    return joinedMesh;
  }

  /// <summary>
  /// Extracting Rhino Mesh and converting to Speckle with the most suitable settings
  /// </summary>
  /// <returns>List of converted Speckle meshes</returns>
  public static List<SOG.Mesh> GetSpeckleMeshes(
    RG.GeometryBase geometry,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    RhinoDoc doc
  )
  {
    RG.Mesh displayMesh = GetGeometryDisplayMesh(geometry, doc);
    List<SOG.Mesh> displayValue = new() { meshConverter.Convert(displayMesh) };
    return displayValue;
  }
}
