namespace Speckle.Converters.Common;

public interface IConverterSettingsStore<T>
  where T : IConverterSettings
{
  T Current { get; }
  IDisposable Push(Func<T> nextContext);
}

public interface IConverterSettings
{
  string SpeckleUnits { get; }
}
