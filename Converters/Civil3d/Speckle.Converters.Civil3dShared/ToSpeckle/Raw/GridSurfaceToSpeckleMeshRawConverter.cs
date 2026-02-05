using Autodesk.AutoCAD.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class GridSurfaceToSpeckleMeshRawConverter : ITypedConverter<CDB.GridSurface, SOG.Mesh>
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public GridSurfaceToSpeckleMeshRawConverter(IConverterSettingsStore<Civil3dConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(object target) => Convert((CDB.GridSurface)target);

  public SOG.Mesh Convert(CDB.GridSurface target)
  {
    List<double> vertices = new();
    List<int> faces = new();
    Dictionary<Point3d, int> indices = new();

    int indexCounter = 0;
    foreach (var cell in target.GetCells(false))
    {
      try
      {
        Point3d[] cellVertices =
        {
          cell.BottomLeftVertex.Location,
          cell.BottomRightVertex.Location,
          cell.TopLeftVertex.Location,
          cell.TopRightVertex.Location,
        };

        foreach (Point3d p in cellVertices)
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

        faces.Add(4);
        faces.Add(indices[cellVertices[0]]);
        faces.Add(indices[cellVertices[1]]);
        faces.Add(indices[cellVertices[2]]);
        faces.Add(indices[cellVertices[3]]);
      }
      finally
      {
        cell.Dispose();
      }
    }

    SOG.Mesh mesh =
      new()
      {
        vertices = vertices,
        faces = faces,
        units = _settingsStore.Current.SpeckleUnits,
      };

    return mesh;
  }
}
