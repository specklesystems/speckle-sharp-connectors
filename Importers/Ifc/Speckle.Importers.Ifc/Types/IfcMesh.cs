using System.Runtime.InteropServices;
using Speckle.Importers.Ifc.Native;

namespace Speckle.Importers.Ifc.Types;

public sealed class IfcMesh(IntPtr mesh)
{
  public int VerticesCount => WebIfc.GetNumVertices(mesh);

  public unsafe ReadOnlySpan<IfcVertex> Vertices
  {
    get
    {
      IfcVertex* ptr = (IfcVertex*)WebIfc.GetVertices(mesh);
      return new ReadOnlySpan<IfcVertex>(ptr, VerticesCount);
    }
  }

  public unsafe ReadOnlySpan<double> Transform
  {
    get
    {
      double* ptr = (double*)WebIfc.GetTransform(mesh);
      return new ReadOnlySpan<double>(ptr, 16);
    }
  }

  public int IndicesCount => WebIfc.GetNumIndices(mesh);

  public unsafe ReadOnlySpan<int> Indices
  {
    get
    {
      var ptr = (int*)WebIfc.GetIndices(mesh);
      return new ReadOnlySpan<int>(ptr, IndicesCount);
    }
  }

  public IfcColor Color => Marshal.PtrToStructure<IfcColor>(WebIfc.GetColor(mesh));
}
