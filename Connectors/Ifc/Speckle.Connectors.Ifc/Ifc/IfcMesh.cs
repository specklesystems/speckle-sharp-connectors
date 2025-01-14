namespace Speckle.Connectors.Ifc.Ifc;

public class IfcMesh(IntPtr mesh)
{
  public int VertexCount => WebIfc.WebIfc.GetNumVertices(mesh);

  public unsafe IfcVertex* GetVertices() => (IfcVertex*)WebIfc.WebIfc.GetVertices(mesh);

  public IntPtr Transform => WebIfc.WebIfc.GetTransform(mesh);
  public int IndexCount => WebIfc.WebIfc.GetNumIndices(mesh);

  public unsafe int* GetIndexes() => (int*)WebIfc.WebIfc.GetIndices(mesh);

  public unsafe IfcColor* GetColor() => (IfcColor*)WebIfc.WebIfc.GetColor(mesh);
}
