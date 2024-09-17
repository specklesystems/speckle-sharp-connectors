namespace Speckle.Converters.Common;

public interface IConverterSettingsStore<T>
  where T : class
{
  T Current { get; }
  IDisposable Push(Func<T, T> nextContext);
  void Initialize(T context);
}
