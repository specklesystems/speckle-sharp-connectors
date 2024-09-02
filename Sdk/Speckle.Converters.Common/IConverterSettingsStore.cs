namespace Speckle.Converters.Common;

public interface IConverterSettingsStore<T>
  where T : class, IConverterSettings
{
  T Current { get; }
  IDisposable Push(Func<T> nextContext);
  internal void Pop();
}

#pragma warning disable CA1040
public interface IConverterSettings { }
#pragma warning restore CA1040
