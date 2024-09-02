namespace Speckle.Converters.Common;

public interface IContextStore<T>
{
  T Current { get; }
  System.IDisposable Push(T nextContext);
  internal void Pop();
}

public static class ContextStoreExtensions
{
  public static IDisposable Push<T>(this IContextStore<T> store, Func<T, T> newContext) =>
    store.Push(newContext(store.Current));
}
