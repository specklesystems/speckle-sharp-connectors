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

    var vertices = _pointListConverter.Convert(target.vertices);
    var colors = ConvertVertexColors(target.colors);
    var vertexNormals = ConvertVertexNormals(target.vertexNormals);
    var textureCoordinates = ConvertTextureCoordinates(target.textureCoordinates);
    var vertices = pointListConverter.ConvertToEnum(target.vertices);
    var colors = target.colors.Select(Color.FromArgb).ToArray();

    m.Vertices.AddVertices(vertices);

    if (colors.Length != 0 && !m.VertexColors.SetColors(colors))
    {
      throw new SpeckleException("Failed to set Vertex Colors");
    }

    if (vertexNormals.Length != 0 && !m.Normals.SetNormals(vertexNormals))
    {
      throw new SpeckleException("Failed to set Vertex Normals");
    }

    if (textureCoordinates.Length != 0 && !m.TextureCoordinates.SetTextureCoordinates(textureCoordinates))
    {
      throw new SpeckleException("Failed to set Texture Coordinates");
    }

    AssignMeshFaces(target, m);

    // POC: CNX-9273 There was a piece of code here about Merging co-planar faces that I've removed for now as this setting does not exist yet.

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
          var triangles = MeshTriangulationHelper.TriangulateFace(i, target, false);

          var faceIndices = RhinoPools.IntListPool.Get();
          for (int t = 0; t < triangles.Count; t += 3)
          {
            faceIndices.Add(m.Faces.AddFace(triangles[t], triangles[t + 1], triangles[t + 2]));
          }

          var faces = RhinoPools.IntArrayPool.Rent(n);
          target.faces.CopyTo(i + 1, faces, 0, n);
          RG.MeshNgon ngon = RG.MeshNgon.Create(target.faces.GetRange(i + 1, n), faceIndices);
          RhinoPools.IntListPool.Return(faceIndices); //safe because MeshNgon.Create uses the list but doesn't keep it
          RhinoPools.IntArrayPool.Return(faces); //safe because MeshNgon.Create uses the list but doesn't keep it
          m.Ngons.AddNgon(ngon);
          break;
        }
      }

      i += n + 1;
    }

    // Its important that this is the last step
    m.Faces.CullDegenerateFaces();
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
