using Speckle.Connectors.Logging;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common;

public sealed class ConnectorMetricsFactory : ISdkMetricsFactory, IDisposable
{
  private readonly LoggingMetricsFactory _loggingMetricsFactory = new();

  public void Dispose() => _loggingMetricsFactory.Dispose();

  public ISdkCounter<T> CreateCounter<T>(string name, string? unit = default, string? description = default)
    where T : struct => new ConnectorCounter<T>(_loggingMetricsFactory.CreateCounter<T>(name, unit, description));

  private readonly struct ConnectorCounter<T>(LoggingCounter<T> counter) : ISdkCounter<T>
    where T : struct
  {
    public void Add(T value) => counter.Add(value);

    public void Add(T value, KeyValuePair<string, object?> tag) => counter.Add(value, tag);

    public void Add(T value, KeyValuePair<string, object?> tag1, KeyValuePair<string, object?> tag2) =>
      counter.Add(value, tag1, tag2);

    public void Add(
      T value,
      KeyValuePair<string, object?> tag1,
      KeyValuePair<string, object?> tag2,
      KeyValuePair<string, object?> tag3
    ) => counter.Add(value, tag1, tag2, tag3);

    public void Add(T value, params KeyValuePair<string, object?>[] tags) => counter.Add(value, tags);
  }
}
