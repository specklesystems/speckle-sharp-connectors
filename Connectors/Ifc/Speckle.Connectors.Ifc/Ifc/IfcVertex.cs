using System.Runtime.InteropServices;

namespace Speckle.WebIfc.Importer.Ifc;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IfcVertex
{
  public double PX,
    PY,
    PZ;
  public double NX,
    NY,
    NZ;
}
