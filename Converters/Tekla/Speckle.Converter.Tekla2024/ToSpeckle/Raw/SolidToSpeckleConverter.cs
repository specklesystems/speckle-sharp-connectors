using System.Collections.Generic;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using SOG = Speckle.Objects.Geometry;
using TG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class TeklaMeshConverter : ITypedConverter<TSM.Solid, SOG.Mesh> 
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public TeklaMeshConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(TSM.Solid target)
  {
    var faceEnum = target.GetFaceEnumerator();
    List<double> vertices = new List<double>();
    List<int> faces = new List<int>();
    Dictionary<string, int> uniqueVertices = new Dictionary<string, int>();
    int currentIndex = 0;

    while (faceEnum.MoveNext())
    {
      var face = faceEnum.Current;
      if (face == null) continue;

      var loopEnum = face.GetLoopEnumerator();
      if (!loopEnum.MoveNext()) continue;
            
      var loop = loopEnum.Current;
      if (loop == null) continue;

      // Get the four corners of each rectangular face
      var corners = new List<int>();
      var vertexEnum = loop.GetVertexEnumerator();
            
      while (vertexEnum.MoveNext())
      {
        var vertex = vertexEnum.Current as TG.Point;
        if (vertex == null) continue;

        string vertexKey = $"{vertex.X:F8},{vertex.Y:F8},{vertex.Z:F8}";
                
        if (!uniqueVertices.ContainsKey(vertexKey))
        {
          uniqueVertices[vertexKey] = currentIndex++;
          vertices.Add(vertex.X);
          vertices.Add(vertex.Y);
          vertices.Add(vertex.Z);
        }
                
        corners.Add(uniqueVertices[vertexKey]);
      }

      // Only process rectangular faces (4 corners)
      if (corners.Count == 4)
      {
        // Create two triangles for the rectangle
        // Triangle 1: 0-1-2
        faces.Add(3);
        faces.Add(corners[0]); // First corner
        faces.Add(corners[1]); // Second corner
        faces.Add(corners[2]); // Third corner

        // Triangle 2: 0-2-3
        faces.Add(3);
        faces.Add(corners[0]); // First corner
        faces.Add(corners[2]); // Third corner
        faces.Add(corners[3]); // Fourth corner
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
