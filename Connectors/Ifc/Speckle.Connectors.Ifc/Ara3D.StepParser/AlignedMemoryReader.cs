using System.Diagnostics;
using Ara3D.Buffers;

namespace Ara3D.StepParser;

public static class AlignedMemoryReader
{
  public static unsafe AlignedMemory ReadAllBytes(string path, int bufferSize = 1024 * 1024)
  {
    using var fs = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.Read,
      bufferSize,
      false
    );
    var fileLength = fs.Length;
    if (fileLength > int.MaxValue)
      throw new IOException("File too big: > 2GB");

    var count = (int)fileLength;
    var r = new AlignedMemory(count);
    var pBytes = r.BytePtr;
    while (count > 0)
    {
      var span = new Span<byte>(pBytes, count);
      var n = fs.Read(span);
      if (n == 0)
        break;
      pBytes += n;
      count -= n;
    }

    Debug.Assert(count == 0);
    return r;
  }
}
