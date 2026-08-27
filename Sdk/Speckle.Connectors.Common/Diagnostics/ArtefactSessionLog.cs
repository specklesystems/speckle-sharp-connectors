using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Sdk;

namespace Speckle.Connectors.Common.Diagnostics;

/// <summary>Whether an artefact session is a publish (send) or load (receive) run.</summary>
public enum ArtefactDirection
{
  Send,
  Receive,
}

/// <summary>
/// Per-session diagnostics writer for the Speckle 4.0 artefact pipeline, shared by every connector's send and receive
/// builder. Each run writes a <b>new timestamped pair</b> under <c>%TEMP%\Speckle\sessions\</c> (runs are never
/// overwritten):
/// <list type="bullet">
/// <item>a machine-parseable <c>.ndjson</c> event stream — one record per object
/// (<c>appId</c>/<c>type</c>/<c>status</c>/<c>error</c>/<c>elapsedMs</c>/<c>phase</c>), plus run-level <c>session_start</c>,
/// <c>phase</c>, <c>bundle_stats</c> and <c>session_end</c> records;</item>
/// <item>a human-readable <c>.summary.txt</c> footer — status breakdown, failed objects, slowest objects and phase
/// timings.</item>
/// </list>
/// Designed so a problematic run can be diagnosed offline (the connector <c>ILogger</c> only reaches Seq). Logging is
/// best-effort: any IO failure degrades to a no-op and never breaks the send/receive.
/// <para>
/// This is a development diagnostic: it is only active in <c>DEBUG</c> and <c>LOCAL</c> builds. Release builds get a
/// disabled session that buffers nothing, touches no files and emits no summary, so callers stay unchanged.
/// </para>
/// </summary>
public sealed class ArtefactSessionLog : IDisposable
{
#if DEBUG || LOCAL
  private const bool IS_ENABLED_BY_BUILD = true;
#else
  private const bool IS_ENABLED_BY_BUILD = false;
#endif

  private const int SLOWEST_COUNT = 15;
  private const int MAX_FAILURES_IN_SUMMARY = 200;

  private readonly object _lock = new();
  private readonly Stopwatch _wallClock = Stopwatch.StartNew();
  private readonly DateTime _startedAt = DateTime.Now;
  private readonly ILogger? _logger;

  private readonly string _connector;
  private readonly ArtefactDirection _direction;
  private readonly string? _project;
  private readonly string? _model;
  private readonly string? _versionId;

  private readonly List<string> _lines = new();
  private readonly Dictionary<Status, int> _statusCounts = new();
  private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
  private readonly Dictionary<string, long> _stats = new(StringComparer.Ordinal);
  private readonly List<(string Name, long Ms)> _phaseTimings = new();
  private readonly List<(string AppId, string? Type, string? Error)> _failures = new();
  private readonly List<(string AppId, long Ms)> _slowest = new();
  private readonly Stack<string> _phaseStack = new();

  private bool _disposed;

  /// <summary>False for the Release no-op session: nothing is buffered, written or logged.</summary>
  private bool IsEnabled { get; }

  private string? NdjsonPath { get; }
  private string? SummaryPath { get; }

  private ArtefactSessionLog(
    string connector,
    ArtefactDirection direction,
    string? project,
    string? model,
    string? versionId,
    ILogger? logger,
    bool enabled
  )
  {
    _connector = connector;
    _direction = direction;
    _project = project;
    _model = model;
    _versionId = versionId;
    _logger = logger;
    IsEnabled = enabled;

    if (!enabled)
    {
      _disposed = true; // nothing to flush; Dispose becomes a no-op
      return;
    }

    (NdjsonPath, SummaryPath) = TryResolvePaths(connector, direction, versionId, _startedAt);

    Write(
      "session_start",
      ("connector", connector),
      ("direction", direction.ToString().ToLowerInvariant()),
      ("project", project),
      ("model", model),
      ("versionId", versionId),
      ("startedAt", _startedAt.ToString("o", CultureInfo.InvariantCulture))
    );
  }

  /// <summary>
  /// Opens a new session log for one send/receive run. Always returns a usable instance (file IO is best-effort).
  /// In Release builds the returned session is disabled and does no work.
  /// </summary>
  public static ArtefactSessionLog Start(
    string connector,
    ArtefactDirection direction,
    string? project,
    string? model,
    string? versionId,
    ILogger? logger = null
  ) => new(connector, direction, project, model, versionId, logger, IS_ENABLED_BY_BUILD);

  /// <summary>Records one converted/baked object: its identity, outcome and elapsed time. Failures are also surfaced in the summary.</summary>
  public void RecordObject(string appId, string? type, Status status, string? error = null, long elapsedMs = 0)
  {
    if (!IsEnabled)
    {
      return;
    }
    lock (_lock)
    {
      _statusCounts.TryGetValue(status, out int existing);
      _statusCounts[status] = existing + 1;

      if (status is Status.ERROR or Status.WARNING)
      {
        _failures.Add((appId, type, error));
      }
      TrackSlowest(appId, elapsedMs);

      Write(
        "object",
        ("appId", appId),
        ("type", type),
        ("status", status.ToString()),
        ("error", error),
        ("elapsedMs", elapsedMs),
        ("phase", CurrentPhase)
      );
    }
  }

  /// <summary>Increments a named counter (e.g. <c>"atomicBaked"</c>, <c>"nonGeometricSkipped"</c>) shown in the summary.</summary>
  public void Increment(string counter, long by = 1)
  {
    if (!IsEnabled)
    {
      return;
    }
    lock (_lock)
    {
      _counters.TryGetValue(counter, out long existing);
      _counters[counter] = existing + by;
    }
  }

  /// <summary>Sets a bundle/run statistic (e.g. <c>"objects"</c>, <c>"geometryBlobs"</c>, <c>"definitions"</c>) shown in the summary.</summary>
  public void SetStat(string name, long value)
  {
    if (!IsEnabled)
    {
      return;
    }
    lock (_lock)
    {
      _stats[name] = value;
    }
  }

  /// <summary>Opens a timed phase scope; dispose it to record the elapsed time. Object records made inside are stamped with the phase name.</summary>
  public IDisposable Phase(string name)
  {
    if (!IsEnabled)
    {
      return NoOpScope.Instance;
    }
    lock (_lock)
    {
      _phaseStack.Push(name);
    }
    return new PhaseScope(this, name);
  }

  private string? CurrentPhase => _phaseStack.Count > 0 ? _phaseStack.Peek() : null;

  private void EndPhase(string name, long elapsedMs)
  {
    lock (_lock)
    {
      if (_phaseStack.Count > 0)
      {
        _phaseStack.Pop();
      }
      _phaseTimings.Add((name, elapsedMs));
      Write("phase", ("name", name), ("elapsedMs", elapsedMs));
    }
  }

  private void TrackSlowest(string appId, long elapsedMs)
  {
    if (elapsedMs <= 0)
    {
      return;
    }
    if (_slowest.Count < SLOWEST_COUNT)
    {
      _slowest.Add((appId, elapsedMs));
      return;
    }
    int minIndex = 0;
    for (int i = 1; i < _slowest.Count; i++)
    {
      if (_slowest[i].Ms < _slowest[minIndex].Ms)
      {
        minIndex = i;
      }
    }
    if (elapsedMs > _slowest[minIndex].Ms)
    {
      _slowest[minIndex] = (appId, elapsedMs);
    }
  }

  public void Dispose()
  {
    lock (_lock)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      _wallClock.Stop();

      Write(
        "session_end",
        ("totalElapsedMs", _wallClock.ElapsedMilliseconds),
        ("success", StatusCount(Status.SUCCESS)),
        ("warning", StatusCount(Status.WARNING)),
        ("error", StatusCount(Status.ERROR))
      );

      var summary = BuildSummary();
      _logger?.LogInformation("Artefact {Direction} session summary:\n{Summary}", _direction, summary);
      TryWriteFile(NdjsonPath, string.Join(Environment.NewLine, _lines));
      TryWriteFile(SummaryPath, summary);
    }
  }

  private int StatusCount(Status status)
  {
    _statusCounts.TryGetValue(status, out int n);
    return n;
  }

  // ── summary ───────────────────────────────────────────────────────────────────────────────────────────
  private string BuildSummary()
  {
    var sb = new StringBuilder();
    sb.AppendLine($"=== Speckle 4.0 artefact {_direction.ToString().ToLowerInvariant()} — {_startedAt:s} ===");
    sb.AppendLine($"connector: {_connector}");
    sb.AppendLine($"project:   {_project}");
    sb.AppendLine($"model:     {_model}");
    sb.AppendLine($"versionId: {_versionId}");
    sb.AppendLine($"total elapsed: {_wallClock.ElapsedMilliseconds} ms");

    sb.AppendLine("--- objects ---");
    sb.AppendLine($"  success: {StatusCount(Status.SUCCESS)}");
    sb.AppendLine($"  warning: {StatusCount(Status.WARNING)}");
    sb.AppendLine($"  error:   {StatusCount(Status.ERROR)}");

    if (_stats.Count > 0)
    {
      sb.AppendLine("--- bundle stats ---");
      foreach (var kv in _stats.OrderBy(k => k.Key, StringComparer.Ordinal))
      {
        sb.AppendLine($"  {kv.Key}: {kv.Value}");
      }
    }

    if (_counters.Count > 0)
    {
      sb.AppendLine("--- counters ---");
      foreach (var kv in _counters.OrderBy(k => k.Key, StringComparer.Ordinal))
      {
        sb.AppendLine($"  {kv.Key}: {kv.Value}");
      }
    }

    if (_phaseTimings.Count > 0)
    {
      sb.AppendLine("--- phase timings ---");
      foreach (var (name, ms) in _phaseTimings)
      {
        sb.AppendLine($"  {name}: {ms} ms");
      }
    }

    if (_slowest.Count > 0)
    {
      sb.AppendLine($"--- slowest objects (top {SLOWEST_COUNT}) ---");
      foreach (var (appId, ms) in _slowest.OrderByDescending(s => s.Ms))
      {
        sb.AppendLine($"  {ms} ms  {appId}");
      }
    }

    if (_failures.Count > 0)
    {
      sb.AppendLine($"--- failed/warning objects ({_failures.Count}) ---");
      foreach (var (appId, type, error) in _failures.Take(MAX_FAILURES_IN_SUMMARY))
      {
        sb.AppendLine($"  [{type}] {appId}: {error}");
      }
      if (_failures.Count > MAX_FAILURES_IN_SUMMARY)
      {
        sb.AppendLine($"  … and {_failures.Count - MAX_FAILURES_IN_SUMMARY} more (see .ndjson).");
      }
    }

    return sb.ToString();
  }

  // ── ndjson plumbing ─────────────────────────────────────────────────────────────────────────────────────
  private void Write(string kind, params (string Key, object? Value)[] fields)
  {
    var sb = new StringBuilder();
    sb.Append("{\"kind\":").Append(JsonString(kind));
    sb.Append(",\"ts\":").Append(JsonString(DateTime.Now.ToString("o", CultureInfo.InvariantCulture)));
    foreach (var (key, value) in fields)
    {
      sb.Append(',').Append(JsonString(key)).Append(':');
      AppendJsonValue(sb, value);
    }
    sb.Append('}');
    _lines.Add(sb.ToString());
  }

  private static void AppendJsonValue(StringBuilder sb, object? value)
  {
    switch (value)
    {
      case null:
        sb.Append("null");
        break;
      case bool b:
        sb.Append(b ? "true" : "false");
        break;
      case long l:
        sb.Append(l.ToString(CultureInfo.InvariantCulture));
        break;
      case int i:
        sb.Append(i.ToString(CultureInfo.InvariantCulture));
        break;
      case double d:
        sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        break;
      default:
        sb.Append(JsonString(value.ToString() ?? string.Empty));
        break;
    }
  }

  private static string JsonString(string value)
  {
    var sb = new StringBuilder(value.Length + 2);
    sb.Append('"');
    foreach (char c in value)
    {
      switch (c)
      {
        case '"':
          sb.Append("\\\"");
          break;
        case '\\':
          sb.Append("\\\\");
          break;
        case '\b':
          sb.Append("\\b");
          break;
        case '\f':
          sb.Append("\\f");
          break;
        case '\n':
          sb.Append("\\n");
          break;
        case '\r':
          sb.Append("\\r");
          break;
        case '\t':
          sb.Append("\\t");
          break;
        default:
          if (c < ' ')
          {
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
          }
          else
          {
            sb.Append(c);
          }
          break;
      }
    }
    sb.Append('"');
    return sb.ToString();
  }

  private static (string? Ndjson, string? Summary) TryResolvePaths(
    string connector,
    ArtefactDirection direction,
    string? versionId,
    DateTime startedAt
  )
  {
    try
    {
      var dir = Path.Combine(Path.GetTempPath(), "Speckle", "sessions");
      Directory.CreateDirectory(dir);
      var ts = startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
      var version = string.IsNullOrEmpty(versionId) ? "noversion" : Sanitize(versionId!);
      var baseName = $"{ts}-{Sanitize(connector)}-{direction.ToString().ToLowerInvariant()}-{version}";
      return (Path.Combine(dir, baseName + ".ndjson"), Path.Combine(dir, baseName + ".summary.txt"));
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return (null, null);
    }
  }

  private void TryWriteFile(string? path, string content)
  {
    if (path is null)
    {
      return;
    }
    try
    {
      File.WriteAllText(path, content);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger?.LogError(ex, "Could not write artefact session diagnostics to {Path}", path);
    }
  }

  private static string Sanitize(string value)
  {
    var invalid = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(value.Length);
    foreach (char c in value)
    {
      sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
    }
    return sb.ToString();
  }

  private sealed class NoOpScope : IDisposable
  {
    public static readonly NoOpScope Instance = new();

    public void Dispose() { }
  }

  private sealed class PhaseScope : IDisposable
  {
    private readonly ArtefactSessionLog _owner;
    private readonly string _name;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private bool _ended;

    public PhaseScope(ArtefactSessionLog owner, string name)
    {
      _owner = owner;
      _name = name;
    }

    public void Dispose()
    {
      if (_ended)
      {
        return;
      }
      _ended = true;
      _sw.Stop();
      _owner.EndPhase(_name, _sw.ElapsedMilliseconds);
    }
  }
}
