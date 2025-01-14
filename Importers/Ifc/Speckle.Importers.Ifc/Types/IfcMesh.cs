namespace Speckle.Importers.Ifc.Types;

public class IfcMesh(IntPtr mesh)
{
  public int VertexCount => Importers.Ifc.Native.WebIfc.GetNumVertices(mesh);

  public unsafe IfcVertex* GetVertices() => (IfcVertex*)Importers.Ifc.Native.WebIfc.GetVertices(mesh);

  public IntPtr Transform => Importers.Ifc.Native.WebIfc.GetTransform(mesh);
  public int IndexCount => Importers.Ifc.Native.WebIfc.GetNumIndices(mesh);

  public unsafe int* GetIndexes() => (int*)Importers.Ifc.Native.WebIfc.GetIndices(mesh);

  public unsafe IfcColor* GetColor() => (IfcColor*)Importers.Ifc.Native.WebIfc.GetColor(mesh);
}
