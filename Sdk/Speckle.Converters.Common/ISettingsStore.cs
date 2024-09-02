namespace Speckle.Converters.Common;

public interface ISettingsStore<T>
{
  T Current { get; }
  System.IDisposable Push(T nextContext);
  internal void Pop();
}

public static class SettingsStoreExtensions
{
  public static IDisposable Push<T>(this ISettingsStore<T> store, Func<T, T> newContext) =>
    store.Push(newContext(store.Current));
}
