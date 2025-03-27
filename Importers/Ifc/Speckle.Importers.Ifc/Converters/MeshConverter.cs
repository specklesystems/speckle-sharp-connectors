using System.Drawing;
using Speckle.Importers.Ifc.Services;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Geometry;
using Speckle.Objects.Other;
using Speckle.Sdk.Common;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public sealed class MeshConverter(
  IRenderMaterialProxyManager renderMaterialManager,
  IUnitContextManager unitContextManager
) : IMeshConverter
{
  public Mesh Convert(IfcMesh mesh)
  {
    var m = mesh.Transform;
    var vp = mesh.Vertices;
    var ip = mesh.Indices;

    var vertices = new List<double>(vp.Length * 3);
    foreach (var vertex in vp)
    {
      var x = vertex.PX;
      var y = vertex.PY;
      var z = vertex.PZ;

      vertices.Add(m[0] * x + m[4] * y + m[8] * z + m[12]);
      vertices.Add(-(m[2] * x + m[6] * y + m[10] * z + m[14]));
      vertices.Add(m[1] * x + m[5] * y + m[9] * z + m[13]);
    }

    var faces = new List<int>(ip.Length * 4);
    for (var i = 0; i < ip.Length; i += 3)
    {
      var a = ip[i];
      var b = ip[i + 1];
      var c = ip[i + 2];
      faces.Add(3);
      faces.Add(a);
      faces.Add(b);
      faces.Add(c);
    }

    RenderMaterial renderMaterial = ConvertRenderMaterial(mesh);
    Mesh converted =
      new()
      {
        applicationId = Guid.NewGuid().ToString(),
        vertices = vertices,
        faces = faces,
        units = unitContextManager.Units.NotNull(),
      };

    renderMaterialManager.AddMeshMapping(renderMaterial, converted);

    return converted;
  }

  private static RenderMaterial ConvertRenderMaterial(IfcMesh mesh)
  {
    var color = mesh.Color;
    var diffuse = Color.FromArgb(1, To8BitValue(color.R), To8BitValue(color.G), To8BitValue(color.B));

    var name = $"IFC_MATERIAL:{(color.A, color.R, color.G, color.B).GetHashCode()}";

    return new RenderMaterial()
    {
      applicationId = name,
      name = name,
      diffuse = diffuse.ToArgb(),
      opacity = color.A
    };
    static int To8BitValue(double value) => (int)(value * 255);
  }
}
