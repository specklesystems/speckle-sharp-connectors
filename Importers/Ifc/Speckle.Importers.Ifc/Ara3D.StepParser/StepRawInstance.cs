using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ara3D.Buffers;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

/// <summary>
/// Contains information about where an instance is within a file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly unsafe struct StepRawInstance(uint id, ByteSpan type, byte* ptr)
{
  public readonly uint Id = id;
  public readonly ByteSpan Type = type;
  public readonly byte* Ptr = ptr;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsValid() => Id > 0;
}
