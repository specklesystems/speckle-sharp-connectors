namespace Speckle.Connectors.Logging;

/// <summary>
/// Configuration object for the Speckle logging system.
/// </summary>

public record SpeckleObservability(SpeckleLogging? Logging = null, SpeckleTracing? Tracing = null);

public record SpeckleLogging(
  SpeckleLogLevel MinimumLevel = SpeckleLogLevel.Warning,
  bool Console = true,
  SpeckleFileLogging? File = null,
  SpeckleOtelLogging? Otel = null
);

public record SpeckleFileLogging(string? Path = null, bool Enabled = true);

public record SpeckleOtelLogging(string Endpoint, bool Enabled = true, Dictionary<string, string>? Headers = null);

public record SpeckleTracing(bool Console = false, SpeckleOtelTracing? Otel = null);

public record SpeckleOtelTracing(
  string? Endpoint = null,
  bool Enabled = true,
  Dictionary<string, string>? Headers = null
);
