using System.Drawing;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Utils;
using Speckle.Sdk;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class MeshToHostConverter(IFlatPointListToHostConverter pointListConverter) : ITypedConverter<SOG.Mesh, RG.Mesh>
{
  /// <summary>
  /// Converts a Speckle mesh object to a Rhino mesh object.
  /// </summary>
  /// <param name="target">The Speckle mesh object to convert.</param>
  /// <returns>A Rhino mesh object converted from the Speckle mesh.</returns>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.Mesh Convert(SOG.Mesh target)
  {
    RG.Mesh m = new();

    var vertices = pointListConverter.ConvertToEnum(target.vertices);

    m.Vertices.AddVertices(vertices);

    if (target.colors.Count != 0)
    {
      var colors = ConvertVertexColors(target.colors);
      if (!m.VertexColors.SetColors(colors))
      {
        throw new SpeckleException("Failed to set Vertex Colors");
      }
    }

    if (target.vertexNormals.Count != 0)
    {
      var vertexNormals = ConvertVertexNormals(target.vertexNormals);
      if (!m.Normals.SetNormals(vertexNormals))
      {
        throw new SpeckleException("Failed to set Vertex Normals");
      }
    }

    if (target.textureCoordinates.Count != 0)
    {
      var textureCoordinates = ConvertTextureCoordinates(target.textureCoordinates);
      if (!m.TextureCoordinates.SetTextureCoordinates(textureCoordinates))
      {
        throw new SpeckleException("Failed to set Texture Coordinates");
      }
    }

    AssignMeshFaces(target, m);

    return m;
  }

  private static void AssignMeshFaces(SOG.Mesh target, RG.Mesh m)
  {
    int i = 0;
    while (i < target.faces.Count)
    {
      int n = target.faces[i];

      // For backwards compatibility. Old meshes will have "0" for triangle face, "1" for quad face.
      // Newer meshes have "3" for triangle face, "4" for quad" face and "5...n" for n-gon face.
      if (n < 3)
      {
        n += 3; // 0 -> 3, 1 -> 4
      }

      switch (n)
      {
        case 3:
          // triangle
          m.Faces.AddFace(target.faces[i + 1], target.faces[i + 2], target.faces[i + 3]);
          break;
        case 4:
          // quad
          m.Faces.AddFace(target.faces[i + 1], target.faces[i + 2], target.faces[i + 3], target.faces[i + 4]);
          break;
        default:
        {
          // n-gon
          var faceIndices = GetNgonFaceIndices(target, i, m).ToList();
          var vertexIndices = GetNgonVertexIndices(target.faces, i, n).ToList();
          RG.MeshNgon ngon = RG.MeshNgon.Create(vertexIndices, faceIndices);
          m.Ngons.AddNgon(ngon);
          break;
        }
      }

      i += n + 1;
    }

    // Its important that this is the last step
    m.Faces.CullDegenerateFaces();
  }

  private static IEnumerable<int> GetNgonFaceIndices(SOG.Mesh target, int start, RG.Mesh m)
  {
    var triangles = MeshTriangulationHelper.TriangulateFace(start, target, false);
    for (int t = 0; t < triangles.Count; t += 3)
    {
      int faceIndex = m.Faces.AddFace(triangles[t], triangles[t + 1], triangles[t + 2]);
      yield return faceIndex;
    }
  }

  private static IEnumerable<int> GetNgonVertexIndices(List<int> faces, int start, int vertexCount)
  {
    for (int n = 0; n < vertexCount; n++)
    {
      yield return faces[start + 1 + n];
    }
  }

  private static RG.Point2f[] ConvertTextureCoordinates(IReadOnlyList<double> textureCoordinates)
  {
    var converted = new RG.Point2f[textureCoordinates.Count / 2];
    for (int i = 0, j = 0; i < textureCoordinates.Count; i += 2, j++)
    {
      float u = (float)textureCoordinates[i];
      float v = (float)textureCoordinates[i + 1];
      converted[j] = new RG.Point2f(u, v);
    }

    return converted;
  }

  private static RG.Vector3f[] ConvertVertexNormals(IReadOnlyList<double> vertexNormals)
  {
    RG.Vector3f[] converted = new RG.Vector3f[vertexNormals.Count / 3];
    for (int i = 0, j = 0; i < vertexNormals.Count; i += 3, j++)
    {
      var x = (float)vertexNormals[i + 0];
      var y = (float)vertexNormals[i + 1];
      var z = (float)vertexNormals[i + 2];
      converted[j] = new RG.Vector3f(x, y, z);
    }

    return converted;
  }

  private static Color[] ConvertVertexColors(IReadOnlyList<int> vertexColors)
  {
    return Enumerable.Select(vertexColors, Color.FromArgb).ToArray();
  }
}
