using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Ara3D.Buffers;

namespace Ara3D.StepParser;

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

    // Fully unrolled handling of each bit
    if ((mask & 0x00000001) != 0)
      offsets.Add(index);
    if ((mask & 0x00000002) != 0)
      offsets.Add(index + 1);
    if ((mask & 0x00000004) != 0)
      offsets.Add(index + 2);
    if ((mask & 0x00000008) != 0)
      offsets.Add(index + 3);
    if ((mask & 0x00000010) != 0)
      offsets.Add(index + 4);
    if ((mask & 0x00000020) != 0)
      offsets.Add(index + 5);
    if ((mask & 0x00000040) != 0)
      offsets.Add(index + 6);
    if ((mask & 0x00000080) != 0)
      offsets.Add(index + 7);
    if ((mask & 0x00000100) != 0)
      offsets.Add(index + 8);
    if ((mask & 0x00000200) != 0)
      offsets.Add(index + 9);
    if ((mask & 0x00000400) != 0)
      offsets.Add(index + 10);
    if ((mask & 0x00000800) != 0)
      offsets.Add(index + 11);
    if ((mask & 0x00001000) != 0)
      offsets.Add(index + 12);
    if ((mask & 0x00002000) != 0)
      offsets.Add(index + 13);
    if ((mask & 0x00004000) != 0)
      offsets.Add(index + 14);
    if ((mask & 0x00008000) != 0)
      offsets.Add(index + 15);
    if ((mask & 0x00010000) != 0)
      offsets.Add(index + 16);
    if ((mask & 0x00020000) != 0)
      offsets.Add(index + 17);
    if ((mask & 0x00040000) != 0)
      offsets.Add(index + 18);
    if ((mask & 0x00080000) != 0)
      offsets.Add(index + 19);
    if ((mask & 0x00100000) != 0)
      offsets.Add(index + 20);
    if ((mask & 0x00200000) != 0)
      offsets.Add(index + 21);
    if ((mask & 0x00400000) != 0)
      offsets.Add(index + 22);
    if ((mask & 0x00800000) != 0)
      offsets.Add(index + 23);
    if ((mask & 0x01000000) != 0)
      offsets.Add(index + 24);
    if ((mask & 0x02000000) != 0)
      offsets.Add(index + 25);
    if ((mask & 0x04000000) != 0)
      offsets.Add(index + 26);
    if ((mask & 0x08000000) != 0)
      offsets.Add(index + 27);
    if ((mask & 0x10000000) != 0)
      offsets.Add(index + 28);
    if ((mask & 0x20000000) != 0)
      offsets.Add(index + 29);
    if ((mask & 0x40000000) != 0)
      offsets.Add(index + 30);
    if ((mask & 0x80000000) != 0)
      offsets.Add(index + 31);

    // Update lineIndex to the next starting position
    index += 32;
  }

  public static unsafe StepRawInstance ParseLine(byte* ptr, byte* end)
  {
    var start = ptr;
    var cnt = end - ptr;
    const int MIN_LINE_LENGTH = 5;
    if (cnt < MIN_LINE_LENGTH)
      return default;

    // Parse the ID
    if (*ptr++ != '#')
      return default;

    var id = 0u;
    while (ptr < end)
    {
      if (*ptr < '0' || *ptr > '9')
        break;
      id = id * 10 + *ptr - '0';
      ptr++;
    }

    var foundEquals = false;
    while (ptr < end)
    {
      if (*ptr == '=')
        foundEquals = true;

      if (*ptr != (byte)' ' && *ptr != (byte)'=')
        break;

      ptr++;
    }

    if (!foundEquals)
      return default;

    // Parse the entity type name
    var entityStart = ptr;
    while (ptr < end)
    {
      if (!StepTokenizer.IsIdentLookup[*ptr])
        break;
      ptr++;
    }
    if (ptr == entityStart)
      return default;

    var entityType = new ByteSpan(entityStart, ptr);
    return new(id, entityType, start);
  }
}
