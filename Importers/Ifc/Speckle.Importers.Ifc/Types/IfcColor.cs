using System.Runtime.InteropServices;

namespace Speckle.Connectors.Ifc.Types;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IfcColor
{
  public double R,
    G,
    B,
    A;
}
