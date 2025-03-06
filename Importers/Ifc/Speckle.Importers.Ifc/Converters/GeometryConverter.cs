using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public class GeometryConverter(IMeshConverter meshConverter) : IGeometryConverter
{
  public List<Base> Convert(IfcGeometry geometry)
  {
    List<Base> ret = new();
    foreach (var mesh in geometry.GetMeshes())
    {
      ret.Add(meshConverter.Convert(mesh));
    }

    return ret;
  }
}
