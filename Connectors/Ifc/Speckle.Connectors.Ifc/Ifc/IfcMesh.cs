namespace Speckle.WebIfc.Importer.Ifc;

public class IfcMesh(IntPtr mesh)
{
  public int VertexCount => WebIfc.GetNumVertices(mesh);

  public unsafe IfcVertex* GetVertices() => (IfcVertex*)WebIfc.GetVertices(mesh);

  public IntPtr Transform => WebIfc.GetTransform(mesh);
  public int IndexCount => WebIfc.GetNumIndices(mesh);

  public unsafe int* GetIndexes() => (int*)WebIfc.GetIndices(mesh);

  public unsafe IfcColor* GetColor() => (IfcColor*)WebIfc.GetColor(mesh);
}
