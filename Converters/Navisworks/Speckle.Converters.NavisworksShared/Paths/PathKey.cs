namespace Speckle.Converter.Navisworks.Paths;

public readonly record struct PathKey
{
  internal readonly int[] Data;
  internal readonly int Hash;

  public static readonly IEqualityComparer<PathKey> Comparer = new PathKeyComparer();

  private PathKey(int[] data)
  {
    this.Data = data ?? throw new ArgumentNullException(nameof(data));
    Hash = ComputeHash(data);
  }

  public static PathKey FromComArray(Array arr)
  {
    if (arr == null)
    {
      throw new ArgumentNullException(nameof(arr));
    }

    if (arr.Rank != 1)
    {
      throw new ArgumentException("Expected 1D array.", nameof(arr));
    }

    int lb = arr.GetLowerBound(0);
    int len = arr.GetLength(0);

    var data = new int[len];
    for (int i = 0; i < len; i++)
    {
      data[i] = (int)arr.GetValue(lb + i);
    }

    return new PathKey(data);
  }

  private static int ComputeHash(int[] data)
  {
    unchecked
    {
      int h = 17;

      // ReSharper disable once ForCanBeConvertedToForeach
      // ReSharper disable once LoopCanBeConvertedToQuery
      for (int i = 0; i < data.Length; i++)
      {
        h = h * 31 + data[i];
      }

      return h;
    }
  }

  public bool MatchesComArray(Array arr)
  {
    if (Data == null)
    {
      return false;
    }

    if (arr.Rank != 1)
    {
      return false;
    }

    int lb = arr.GetLowerBound(0);
    int len = arr.GetLength(0);
    if (len != Data.Length)
    {
      return false;
    }

    for (int i = 0; i < len; i++)
    {
      if ((int)arr.GetValue(lb + i) != Data[i])
      {
        return false;
      }
    }

    return true;
  }

  public override string ToString()
  {
    if (Data == null || Data.Length == 0)
    {
      return string.Empty;
    }
    return string.Join(",", Data);
  }

  /// <summary>
  /// Returns a compact string representation using the hash value as an unsigned integer.
  /// Suitable for use as application IDs and definition IDs.
  /// This avoids negative numbers in IDs by treating the hash as unsigned.
  /// </summary>
  public string ToHashString() => unchecked((uint)Hash).ToString();
}

internal sealed class PathKeyComparer : IEqualityComparer<PathKey>
{
  public bool Equals(PathKey x, PathKey y)
  {
    if (ReferenceEquals(x.Data, y.Data))
    {
      return true;
    }

    if (x.Data.Length != y.Data.Length)
    {
      return false;
    }

    // ReSharper disable once LoopCanBeConvertedToQuery
    for (int i = 0; i < x.Data.Length; i++)
    {
      if (x.Data[i] != y.Data[i])
      {
        return false;
      }
    }

    return true;
  }

  public int GetHashCode(PathKey obj) => obj.Hash;
}
