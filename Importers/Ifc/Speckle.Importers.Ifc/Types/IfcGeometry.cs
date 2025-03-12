namespace Speckle.Importers.Ifc.Types;

public sealed class IfcGeometry(IntPtr geometry)
{
  public IfcMesh GetMesh(int i) => new(Importers.Ifc.Native.WebIfc.GetMesh(geometry, i));

  public int MeshCount => Importers.Ifc.Native.WebIfc.GetNumMeshes(geometry);

  public IfcSchemaType Type => (IfcSchemaType)Importers.Ifc.Native.WebIfc.GetGeometryType(geometry);

  public IEnumerable<IfcMesh> GetMeshes()
  {
    for (int i = 0; i < MeshCount; ++i)
    {
      yield return GetMesh(i);
    }
  }
}
