namespace Speckle.Connectors.Logging;

/// <summary>
/// Configuration object for the Speckle logging system.
/// </summary>
public record SpeckleObservability(
  SpeckleLogging? Logging = null,
  SpeckleTracing? Tracing = null,
  SpeckleMetrics? Metrics = null
);

public record SpeckleLogging(
  SpeckleLogLevel MinimumLevel = SpeckleLogLevel.Warning,
  bool Console = true,
  SpeckleFileLogging? File = null,
  IEnumerable<SpeckleOtelLogging>? Otel = null
)
{
  public SpeckleLogging(
    SpeckleLogLevel minimumLevel = SpeckleLogLevel.Warning,
    bool console = true,
    SpeckleFileLogging? file = null,
    SpeckleOtelLogging? otel = null
  )
    : this(minimumLevel, console, file, otel is null ? null : [otel]) { }
}

public record SpeckleFileLogging(string? Path = null, bool Enabled = true);

public record SpeckleOtelLogging(
  string? Endpoint = null,
  bool Enabled = true,
  Dictionary<string, string>? Headers = null
);

public record SpeckleTracing(bool Console = false, IEnumerable<SpeckleOtelTracing>? Otel = null)
{
  public SpeckleTracing(bool console = true, SpeckleOtelTracing? otel = null)
    : this(console, otel is null ? null : [otel]) { }
}

public record SpeckleOtelTracing(
  string? Endpoint = null,
  bool Enabled = true,
  Dictionary<string, string>? Headers = null
);

public record SpeckleMetrics(bool Console = false, IEnumerable<SpeckleOtelMetrics>? Otel = null)
{
  public SpeckleMetrics(bool console = true, SpeckleOtelMetrics? otel = null)
    : this(console, otel is null ? null : [otel]) { }
}

public record SpeckleOtelMetrics(
  string? Endpoint = null,
  bool Enabled = true,
  Dictionary<string, string>? Headers = null
);
