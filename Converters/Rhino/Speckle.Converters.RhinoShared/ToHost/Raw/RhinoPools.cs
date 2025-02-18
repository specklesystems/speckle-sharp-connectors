using System.Buffers;
using Speckle.Sdk.Dependencies;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public static class RhinoPools
{
  public static readonly ArrayPool<int> IntArrayPool = ArrayPool<int>.Shared;

  public static readonly Pool<List<int>> IntListPool = Pools.CreateListPool<int>();
}
