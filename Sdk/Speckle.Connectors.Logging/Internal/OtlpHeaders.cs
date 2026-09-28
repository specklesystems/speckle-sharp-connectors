namespace Speckle.Connectors.Logging.Internal;

internal static class OtlpHeaders
{
  /// <summary>
  /// Formats configured headers as the comma-separated <c>key=value</c> list the OTLP exporter options expect.
  /// </summary>
  public static string Join(IEnumerable<KeyValuePair<string, string>>? headers) =>
    string.Join(",", headers?.Select(h => $"{h.Key}={h.Value}") ?? []);
}
