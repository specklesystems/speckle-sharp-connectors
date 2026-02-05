using Autodesk.AutoCAD.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class TinSurfaceToSpeckleMeshRawConverter : ITypedConverter<CDB.TinSurface, SOG.Mesh>
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public TinSurfaceToSpeckleMeshRawConverter(IConverterSettingsStore<Civil3dConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(object target) => Convert((CDB.TinSurface)target);

  public SOG.Mesh Convert(CDB.TinSurface target)
  {
    List<double> vertices = new();
    List<int> faces = new();
    Dictionary<Point3d, int> indices = new();

    int indexCounter = 0;
    foreach (var triangle in target.GetTriangles(false))
    {
      try
      {
        Point3d[] triangleVertices =
        {
          triangle.Vertex1.Location,
          triangle.Vertex2.Location,
          triangle.Vertex3.Location
        };
        foreach (Point3d p in triangleVertices)
        {
          if (indices.ContainsKey(p))
          {
            continue;
          }

          vertices.Add(p.X);
          vertices.Add(p.Y);
          vertices.Add(p.Z);
          indices.Add(p, indexCounter);
          indexCounter++;
        }
        faces.Add(3);
        faces.Add(indices[triangleVertices[0]]);
        faces.Add(indices[triangleVertices[1]]);
        faces.Add(indices[triangleVertices[2]]);
      }
      finally
      {
        triangle.Dispose();
      }
    }

    SOG.Mesh mesh =
      new()
      {
        faces = faces,
        vertices = vertices,
        units = _settingsStore.Current.SpeckleUnits
      };

    return mesh;
  }
}
