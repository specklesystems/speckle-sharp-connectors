using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Ara3D.Buffers;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public static class StepLineParser
{
  public static readonly Vector256<byte> Comma = Vector256.Create((byte)',');
  public static readonly Vector256<byte> NewLine = Vector256.Create((byte)'\n');
  public static readonly Vector256<byte> StartGroup = Vector256.Create((byte)'(');
  public static readonly Vector256<byte> EndGroup = Vector256.Create((byte)')');
  public static readonly Vector256<byte> Definition = Vector256.Create((byte)'=');
  public static readonly Vector256<byte> Quote = Vector256.Create((byte)'\'');
  public static readonly Vector256<byte> Id = Vector256.Create((byte)'#');
  public static readonly Vector256<byte> SemiColon = Vector256.Create((byte)';');
  public static readonly Vector256<byte> Unassigned = Vector256.Create((byte)'*');

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void ComputeOffsets(in Vector256<byte> v, ref int index, List<int> offsets)
  {
    var r = Avx2.CompareEqual(v, NewLine);
    var mask = (uint)Avx2.MoveMask(r);
    if (mask == 0)
    {
      index += 32;
      return;
    }

    for (int i = 0; mask != 0; i++, mask >>= 1)
    {
      if ((mask & 1) != 0)
      {
        offsets.Add(index + i);
      }
    }

    // Update lineIndex to the next starting position
    index += 32;
  }

  public static unsafe StepRawInstance ParseLine(byte* ptr, byte* end)
  {
    var start = ptr;
    var cnt = end - ptr;
    const int MIN_LINE_LENGTH = 5;
    if (cnt < MIN_LINE_LENGTH)
    {
      return default;
    }

    // Parse the ID
    if (*ptr++ != '#')
    {
      return default;
    }

    var id = 0u;
    while (ptr < end)
    {
      if (*ptr < '0' || *ptr > '9')
      {
        break;
      }

      id = id * 10 + *ptr - '0';
      ptr++;
    }

    var foundEquals = false;
    while (ptr < end)
    {
      if (*ptr == '=')
      {
        foundEquals = true;
      }

      if (*ptr != (byte)' ' && *ptr != (byte)'=')
      {
        break;
      }

      ptr++;
    }

    if (!foundEquals)
    {
      return default;
    }

    // Parse the entity type name
    var entityStart = ptr;
    while (ptr < end)
    {
      if (!StepTokenizer.IsIdentLookup[*ptr])
      {
        break;
      }

      ptr++;
    }
    if (ptr == entityStart)
    {
      return default;
    }

    var entityType = new ByteSpan(entityStart, ptr);
    return new(id, entityType, start);
  }
}
