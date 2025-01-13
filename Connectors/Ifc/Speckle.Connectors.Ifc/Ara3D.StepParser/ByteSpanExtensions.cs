using System.Runtime.CompilerServices;
using Ara3D.Buffers;

namespace Ara3D.StepParser;

public static class ByteSpanExtensions
{
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static double ToDouble(this ByteSpan self) => double.Parse(self.ToSpan());

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int ToInt(this ByteSpan self) => int.Parse(self.ToSpan());
}
