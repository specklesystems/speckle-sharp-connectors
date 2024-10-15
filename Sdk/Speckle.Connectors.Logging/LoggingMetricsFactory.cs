using System.Diagnostics.Metrics;
using System.Reflection;

namespace Speckle.Connectors.Logging;

public sealed class LoggingMetricsFactory : IDisposable
{
  private readonly Meter _meterSource =
    new(Consts.TRACING_SOURCE, Consts.GetPackageVersion(Assembly.GetExecutingAssembly()));

  public LoggingCounter<T> CreateCounter<T>(string name, string? unit = null, string? description = null)
    where T : struct => new(_meterSource.CreateCounter<T>(name, unit, description));

  public void Dispose() => _meterSource.Dispose();
}

public readonly struct LoggingCounter<T>
  where T : struct
{
  private readonly Counter<T> _counter;

  internal LoggingCounter(Counter<T> counter)
  {
    _counter = counter;
  }

  public void Add(T value) => _counter.Add(value);

  public void Add(T value, KeyValuePair<string, object?> tag) => _counter.Add(value, tag);

  public void Add(T value, KeyValuePair<string, object?> tag1, KeyValuePair<string, object?> tag2) =>
    _counter.Add(value, tag1, tag2);

  public void Add(
    T value,
    KeyValuePair<string, object?> tag1,
    KeyValuePair<string, object?> tag2,
    KeyValuePair<string, object?> tag3
  ) => _counter.Add(value, tag1, tag2, tag3);

  public void Add(T value, params KeyValuePair<string, object?>[] tags) => _counter.Add(value, tags);
}
