using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models.Collections;
using Speckle.WebIfc.Importer.Ifc;

namespace Speckle.WebIfc.Importer.Converters;

[GenerateAutoInterface]
public class GeometryConverter(IMeshConverter meshConverter) : IGeometryConverter
{
  public Collection Convert(IfcGeometry geometry)
  {
    var c = new Collection(ge);
    foreach (var mesh in geometry.GetMeshes())
    {
      c.elements.Add(meshConverter.Convert(mesh));
    }

    return c;
  }
}
