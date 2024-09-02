namespace Speckle.Converters.Common;

public interface IConverterSettingsStore<T>
  where T : class, IConverterSettings
{
  T Current { get; }
  IDisposable Push(Func<T> nextContext);
  internal void Pop();
}

public interface IConverterSettings
{
  string SpeckleUnits { get; }
}
