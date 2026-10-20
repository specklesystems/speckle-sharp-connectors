namespace Speckle.Connectors.Common.Settings;

public static class OtelConfig
{
  private static readonly GlobalConfigHelper s_helper = new(null);

  public static Uri GetOtelLogsExporter(Uri defaultValue)
  {
    if (s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", out string? value))
    {
      return new Uri(value);
    }

    if (s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_ENDPOINT", out value))
    {
      return new Uri(new Uri(value), "/v1/logs");
    }

    return defaultValue;
  }

  public static Uri GetOtelTracesExporter(Uri defaultValue)
  {
    if (s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", out string? value))
    {
      return new Uri(value);
    }

    if (s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_ENDPOINT", out value))
    {
      return new Uri(new Uri(value), "/v1/traces");
    }

    return defaultValue;
  }

  public static string? GetOtelTracesHeaders(string? defaultValue)
  {
    _ = s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_HEADERS", out string? allHeaders);
    _ = s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_TRACES_HEADERS", out string? traceHeaders);

    if (allHeaders is not null && traceHeaders is not null)
    {
      return $"{allHeaders},{traceHeaders}";
    }
    else
    {
      return allHeaders ?? traceHeaders ?? defaultValue;
    }
  }

  public static string? GetOtelLogsHeaders(string? defaultValue)
  {
    _ = s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_HEADERS", out string? allHeaders);
    _ = s_helper.GetConfigValue("SPECKLE_OTEL_EXPORTER_OTLP_LOGS_HEADERS", out string? traceHeaders);

    if (allHeaders is not null && traceHeaders is not null)
    {
      return $"{allHeaders},{traceHeaders}";
    }
    else
    {
      return allHeaders ?? traceHeaders ?? defaultValue;
    }
  }

  public static string GetOtelServiceName(string defaultValue)
  {
    if (s_helper.GetConfigValue("SPECKLE_OTEL_SERVICE_NAME", out string? serviceName))
    {
      return serviceName;
    }

    return defaultValue;
  }
}
