using Autodesk.AutoCAD.Geometry;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class TinSurfaceToSpeckleMeshRawConverter : ITypedConverter<CDB.TinSurface, SOG.Mesh>
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public TinSurfaceToSpeckleMeshRawConverter(
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(object target) => Convert((CDB.TinSurface)target);

  public SOG.Mesh Convert(CDB.TinSurface target)
  {
    // An empty / no-data TinSurface has no built triangle network. Calling GetTriangles() on it reads
    // protected native memory -> AccessViolationException -> fatal host crash that cannot be caught
    // (corrupted-state exception; never delivered to managed catch on .NET 8). Probe the TIN
    // definition first via GetTinProperties(), a safe managed call that does not touch the native
    // triangle buffer. If the probe fails, the network is inaccessible and GetTriangles() would crash,
    // so we throw a catchable SpeckleException -> reported as a per-object conversion error upstream.
    try
    {
      _ = target.GetTinProperties();
    }
    // Catch any non-fatal failure to read the TIN definition (the broken surface throws a managed
    // InvalidOperationException here): the network is inaccessible, so skip the surface rather than
    // risk the native crash. IsFatal() still lets true corrupted-state exceptions propagate.
    catch (Exception ex) when (!ex.IsFatal())
    {
      throw new SpeckleException(
        $"TinSurface '{target.DisplayName}' has no accessible triangle data and cannot be converted safely.",
        ex
      );
    }

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
          triangle.Vertex3.Location,
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

    SOG.Mesh mesh = new()
    {
      faces = faces,
      vertices = _referencePointConverter.ConvertWCSDoublesToExternalCoordinates(vertices), // transform by reference point
      units = _settingsStore.Current.SpeckleUnits,
    };

    return mesh;
  }
}
