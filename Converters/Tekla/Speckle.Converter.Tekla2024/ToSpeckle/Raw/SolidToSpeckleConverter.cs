using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class SolidToSpeckleConverter : ITypedConverter<TSM.Solid, SOG.Mesh>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public SolidToSpeckleConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(TSM.Solid target)
  {
    List<double> vertices = new List<double>();
    List<int> faces = new List<int>();
    Dictionary<TG.Point, int> vertexIndices = new Dictionary<TG.Point, int>();
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

          if (!vertexIndices.TryGetValue(vertex, out int value))
          {
            value = currentIndex++;
            vertexIndices[vertex] = value;
            vertices.Add(vertex.X);
            vertices.Add(vertex.Y);
            vertices.Add(vertex.Z);
          }

          faceVertices.Add(value);
        }

        if (faceVertices.Count >= 3)
        {
          faces.Add(faceVertices.Count);
          faces.AddRange(faceVertices);
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
}
