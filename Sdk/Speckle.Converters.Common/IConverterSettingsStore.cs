namespace Speckle.Converters.Common;

public interface IConverterSettingsStore<T>
{
  T Current { get; }
  IDisposable Push(T nextContext);
  internal void Pop();
}

public static class SettingsStoreExtensions
{
  public static IDisposable Push<T>(this IConverterSettingsStore<T> store, Func<T, T> newContext) =>
    store.Push(newContext(store.Current));
}
