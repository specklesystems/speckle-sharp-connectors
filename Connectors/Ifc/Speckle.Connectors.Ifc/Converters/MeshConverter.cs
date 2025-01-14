using Speckle.Connectors.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Geometry;

namespace Speckle.Connectors.Ifc.Converters;

[GenerateAutoInterface]
public class MeshConverter : IMeshConverter
{
  public unsafe Mesh Convert(IfcMesh mesh)
  {
    var m = (double*)mesh.Transform;
    var vp = mesh.GetVertices();
    var ip = mesh.GetIndexes();

    var vertices = new List<double>(mesh.VertexCount * 3);
    for (var i = 0; i < mesh.VertexCount; i++)
    {
      var x = vp[i].PX;
      var y = vp[i].PY;
      var z = vp[i].PZ;
      vertices.Add(m[0] * x + m[4] * y + m[8] * z + m[12]);
      vertices.Add(-(m[2] * x + m[6] * y + m[10] * z + m[14]));
      vertices.Add(m[1] * x + m[5] * y + m[9] * z + m[13]);
    }

    var faces = new List<int>(mesh.IndexCount * 4);
    for (var i = 0; i < mesh.IndexCount; i += 3)
    {
      var a = ip[i];
      var b = ip[i + 1];
      var c = ip[i + 2];
      faces.Add(3);
      faces.Add(a);
      faces.Add(b);
      faces.Add(c);
    }

    var color = mesh.GetColor();
    List<int> colors =
    [
      (int)(color->A * 255),
      (int)(color->R * 255),
      (int)(color->G * 255),
      (int)(color->B * 255),
    ];
    return new Mesh()
    {
      colors = colors,
      vertices = vertices,
      faces = faces,
      units = "m",
    };
  }
}
