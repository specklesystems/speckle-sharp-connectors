namespace Speckle.Connectors.Common.Extensions;

public static class CollectionExtensions
{
  public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
  {
    foreach (var item in items)
    {
      collection.Add(item);
    }
  }

#if NETSTANDARD2_0
  public static HashSet<T> ToHashSet<T>(this IEnumerable<T> items)
  {
    var set = new HashSet<T>(items);
    return set;
  }
#endif
}
