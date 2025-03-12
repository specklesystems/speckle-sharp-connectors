using System.Runtime.InteropServices;

namespace Speckle.Importers.Ifc.Types;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct IfcVertex
{
  public readonly double PX,
    PY,
    PZ;
  public readonly double NX,
    NY,
    NZ;
}
