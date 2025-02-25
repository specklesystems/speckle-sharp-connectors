using System.Buffers;
using Speckle.Sdk.Dependencies;

namespace Speckle.Converters.Rhino.ToHost.Raw;

/// <summary>
/// These are pools used by Rhino for temporary collections to pool them instead of allocating each time.  This is faster and saves memory/GC time.
/// </summary>
public static class RhinoPools
{
  public static readonly ArrayPool<int> IntArrayPool = ArrayPool<int>.Shared;

  public static readonly Pool<List<int>> IntListPool = Pools.CreateListPool<int>();
}
