using System.Runtime.InteropServices;

namespace Speckle.Importers.Ifc.Types;

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
