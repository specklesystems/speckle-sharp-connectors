namespace Speckle.WebIfc.Importer.Ifc;

public class IfcGeometry(IntPtr geometry)
{
  public IfcMesh GetMesh(int i) => new(WebIfc.GetMesh(geometry, i));

  public int MeshCount => WebIfc.GetNumMeshes(geometry);

  public IfcSchemaType Type => (IfcSchemaType)WebIfc.GetGeometryType(geometry);

  public IEnumerable<IfcMesh> GetMeshes()
  {
    for (int i = 0; i < MeshCount; ++i)
    {
      yield return GetMesh(i);
    }
  }
}
