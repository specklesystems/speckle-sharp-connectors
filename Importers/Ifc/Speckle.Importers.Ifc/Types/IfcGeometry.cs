namespace Speckle.Connectors.Ifc.Types;

public class IfcGeometry(IntPtr geometry)
{
  public IfcMesh GetMesh(int i) => new(WebIfc.WebIfc.GetMesh(geometry, i));

  public int MeshCount => WebIfc.WebIfc.GetNumMeshes(geometry);

  public IfcSchemaType Type => (IfcSchemaType)WebIfc.WebIfc.GetGeometryType(geometry);

  public IEnumerable<IfcMesh> GetMeshes()
  {
    for (int i = 0; i < MeshCount; ++i)
    {
      yield return GetMesh(i);
    }
  }
}
