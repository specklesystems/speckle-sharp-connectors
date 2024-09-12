namespace Speckle.Connectors.Logging;

/// <summary>
/// Configuration object for the Speckle logging system.
/// </summary>

public record SpeckleTracing(bool Console = false, SpeckleOtelTracing? Otel = null);

public record SpeckleOtelTracing(
  string? Endpoint = null,
  bool Enabled = true,
  Dictionary<string, string>? Headers = null
);
