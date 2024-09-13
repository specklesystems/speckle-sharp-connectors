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

public enum SpeckleLogLevel
{
  /// <summary>
  /// Anything and everything you might want to know about
  /// a running block of code.
  /// </summary>
  Verbose,

  /// <summary>
  /// Internal system events that aren't necessarily
  /// observable from the outside.
  /// </summary>
  Debug,

  /// <summary>
  /// The lifeblood of operational intelligence - things
  /// happen.
  /// </summary>
  Information,

  /// <summary>
  /// Service is degraded or endangered.
  /// </summary>
  Warning,

  /// <summary>
  /// Functionality is unavailable, invariants are broken
  /// or data is lost.
  /// </summary>
  Error,

  /// <summary>
  /// If you have a pager, it goes off when one of these
  /// occurs.
  /// </summary>
  Fatal
}
