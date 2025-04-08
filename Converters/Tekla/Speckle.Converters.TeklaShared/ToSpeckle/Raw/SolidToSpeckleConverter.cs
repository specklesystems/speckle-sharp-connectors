using Speckle.Common.MeshTriangulation;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Common;
using Tekla.Structures.Solid;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class SolidToSpeckleConverter : ITypedConverter<TSM.Solid, SOG.Mesh>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public SolidToSpeckleConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(TSM.Solid target)
  {
    double conversionFactor = Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);

    // if there are exactly 2 opposite facing contours with holes (inner loops)
    var facesAsPolygons = GetFacesAsPolygons(target);
    var facesToExtrude = facesAsPolygons.Where(lst => lst.Count > 1).ToList();
    if (facesToExtrude.Count == 2)
    {
      var n0 = facesToExtrude[0][0].GetNormal();
      var n1 = facesToExtrude[1][0].GetNormal();

      if ((n0 + n1).Length() < 0.001)
      {
        return ExtrudeFromPolygons(facesToExtrude[0], facesToExtrude[1]);
      }
    }

    List<double> vertices = new List<double>();
    List<int> faces = new List<int>();
    int currentIndex = 0;

    var faceEnum = target.GetFaceEnumerator();
    while (faceEnum.MoveNext())
    {
      var face = faceEnum.Current;
      if (face == null)
      {
        continue;
      }

      var loopEnum = face.GetLoopEnumerator();
      while (loopEnum.MoveNext())
      {
        var loop = loopEnum.Current;
        if (loop == null)
        {
          continue;
        }

        var faceVertices = new List<int>();
        var vertexEnum = loop.GetVertexEnumerator();

        while (vertexEnum.MoveNext())
        {
          var vertex = vertexEnum.Current;
          if (vertex == null)
          {
            continue;
          }

          int index = currentIndex++;
          vertices.Add(vertex.X * conversionFactor);
          vertices.Add(vertex.Y * conversionFactor);
          vertices.Add(vertex.Z * conversionFactor);
          faceVertices.Add(index);
        }

        if (faceVertices.Count >= 3)
        {
          faces.Add(faceVertices.Count);
          // NOTE: normals were flipped in tekla logic
          // we changed the order of the vertices here
          for (int i = faceVertices.Count - 1; i >= 0; i--)
          {
            faces.Add(faceVertices[i]);
          }
        }
      }
    }

    return new SOG.Mesh
    {
      vertices = vertices,
      faces = faces,
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  private SOG.Mesh ExtrudeFromPolygons(List<Poly3> face1, List<Poly3> face2)
  {
    var point = face2[0].Vertices[0];
    var generator = new MeshGenerator(new BaseTransformer(), new LibTessTriangulator());
    var mesh3 = generator.ExtrudeMesh(face1, point);

    return Mesh3ToSpeckleMesh(mesh3);
  }

  private SOG.Mesh Mesh3ToSpeckleMesh(Mesh3 mesh3)
  {
    double conversionFactor = Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);
    var vertices = new List<double>();
    var faces = new List<int>();

    foreach (var v in mesh3.Vertices)
    {
      vertices.Add(v.X * conversionFactor);
      vertices.Add(v.Y * conversionFactor);
      vertices.Add(v.Z * conversionFactor);
    }

    for (int i = 0; i < mesh3.Triangles.Count; i += 3)
    {
      faces.Add(3);
      faces.Add(mesh3.Triangles[i]);
      faces.Add(mesh3.Triangles[i + 1]);
      faces.Add(mesh3.Triangles[i + 2]);
    }

    var mesh = new SOG.Mesh
    {
      vertices = vertices,
      faces = faces,
      units = _settingsStore.Current.SpeckleUnits
    };

    return mesh;
  }

  private static List<List<Poly3>> GetFacesAsPolygons(TSM.Solid solid)
  {
    var polyFaces = new List<List<Poly3>>();

    FaceEnumerator faceEnum = solid.GetFaceEnumerator();
    while (faceEnum.MoveNext())
    {
      Face face = faceEnum.Current;
      LoopEnumerator loopEnum = face.GetLoopEnumerator();

      var polyFace = new List<Poly3>();
      while (loopEnum.MoveNext())
      {
        Loop loop = loopEnum.Current;
        VertexEnumerator vertexEnum = loop.GetVertexEnumerator();

        var vertices = new List<Vector3>();
        while (vertexEnum.MoveNext())
        {
          Tekla.Structures.Geometry3d.Point point = vertexEnum.Current;
          vertices.Add(new Vector3(point.X, point.Y, point.Z));
        }
        polyFace.Add(new Poly3(vertices));
      }

      polyFaces.Add(polyFace);
    }

    return polyFaces;
  }
}
