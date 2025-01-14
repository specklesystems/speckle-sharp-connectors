using System.Runtime.InteropServices;

namespace Speckle.Connectors.Ifc.Ifc;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IfcColor
{
  public double R,
    G,
    B,
    A;
}
