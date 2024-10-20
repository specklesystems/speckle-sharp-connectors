﻿using System.Drawing;
using Rhino.Collections;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Utils;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class MeshToHostConverter : ITypedConverter<SOG.Mesh, RG.Mesh>
{
  private readonly ITypedConverter<IReadOnlyList<double>, Point3dList> _pointListConverter;

  public MeshToHostConverter(ITypedConverter<IReadOnlyList<double>, Point3dList> pointListConverter)
  {
    _pointListConverter = pointListConverter;
  }

  /// <summary>
  /// Converts a Speckle mesh object to a Rhino mesh object.
  /// </summary>
  /// <param name="target">The Speckle mesh object to convert.</param>
  /// <returns>A Rhino mesh object converted from the Speckle mesh.</returns>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.Mesh Convert(SOG.Mesh target)
  {
    target.AlignVerticesWithTexCoordsByIndex();

    RG.Mesh m = new();

    var vertices = _pointListConverter.Convert(target.vertices);
    var colors = target.colors.Select(Color.FromArgb).ToArray();

    m.Vertices.AddVertices(vertices);
    m.VertexColors.SetColors(colors);

    AssignMeshFaces(target, m);
    AssignTextureCoordinates(target, m);

    // POC: CNX-9273 There was a piece of code here about Merging co-planar faces that I've removed for now as this setting does not exist yet.

    return m;
  }

  // POC: CNX-9274 We should abstract this into the `Mesh` class, or some utility class adjacent to it
  //      All converters need to do this so it's going to be a source of repetition
  //      and it is directly tied to how we serialise the data in the mesh.
  private void AssignMeshFaces(SOG.Mesh target, RG.Mesh m)
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

      if (n == 3)
      {
        // triangle
        m.Faces.AddFace(new RG.MeshFace(target.faces[i + 1], target.faces[i + 2], target.faces[i + 3]));
      }
      else if (n == 4)
      {
        // quad
        m.Faces.AddFace(
          new RG.MeshFace(target.faces[i + 1], target.faces[i + 2], target.faces[i + 3], target.faces[i + 4])
        );
      }
      else
      {
        // n-gon
        var triangles = MeshTriangulationHelper.TriangulateFace(i, target, false);

        var faceIndices = new List<int>(triangles.Count);
        for (int t = 0; t < triangles.Count; t += 3)
        {
          var face = new RG.MeshFace(triangles[t], triangles[t + 1], triangles[t + 2]);
          faceIndices.Add(m.Faces.AddFace(face));
        }

        RG.MeshNgon ngon = RG.MeshNgon.Create(target.faces.GetRange(i + 1, n), faceIndices);
        m.Ngons.AddNgon(ngon);
      }

      i += n + 1;
    }
    m.Faces.CullDegenerateFaces();
  }

  private void AssignTextureCoordinates(SOG.Mesh target, RG.Mesh m)
  {
    var textureCoordinates = new RG.Point2f[target.TextureCoordinatesCount];
    for (int ti = 0; ti < target.TextureCoordinatesCount; ti++)
    {
      var (u, v) = target.GetTextureCoordinate(ti);
      textureCoordinates[ti] = new RG.Point2f(u, v);
    }
    m.TextureCoordinates.SetTextureCoordinates(textureCoordinates);
  }
}
