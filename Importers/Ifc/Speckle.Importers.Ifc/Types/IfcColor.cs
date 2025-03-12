using System.Runtime.InteropServices;

namespace Speckle.Importers.Ifc.Types;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct IfcColor
{
  public readonly double R,
    G,
    B,
    A;
}
