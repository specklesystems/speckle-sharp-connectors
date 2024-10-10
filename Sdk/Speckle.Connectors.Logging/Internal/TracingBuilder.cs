using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Speckle.Connectors.Logging.Internal;

internal static class TracingBuilder
{
  public static IDisposable? Initialize(SpeckleTracing? logConfiguration, ResourceBuilder resourceBuilder)
  {
    var consoleEnabled = logConfiguration?.Console ?? false;
    var otelEnabled = logConfiguration?.Otel?.Enabled ?? false;
    if (!consoleEnabled && !otelEnabled)
    {
      return null;
    }

    var tracerProviderBuilder = OpenTelemetry
      .Sdk.CreateTracerProviderBuilder()
      .AddSource(LoggingActivityFactory.TRACING_SOURCE);
    tracerProviderBuilder = tracerProviderBuilder.AddHttpClientInstrumentation();
    if (otelEnabled)
    {
      tracerProviderBuilder = tracerProviderBuilder.AddOtlpExporter(x => ProcessOptions(logConfiguration!, x));
    }

    if (consoleEnabled)
    {
      tracerProviderBuilder = tracerProviderBuilder.AddConsoleExporter();
    }

    tracerProviderBuilder = tracerProviderBuilder
      .SetResourceBuilder(resourceBuilder)
      .SetSampler<AlwaysOnSampler>()
      .AddProcessor(new ActivityScopeProcessor());

    return tracerProviderBuilder.Build();
  }

  private static void ProcessOptions(SpeckleTracing logConfiguration, OtlpExporterOptions options)
  {
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    var headers = string.Join(",", logConfiguration.Otel?.Headers?.Select(x => x.Key + "=" + x.Value) ?? []);
    if (headers.Length != 0)
    {
      options.Headers = headers;
    }

    if (logConfiguration.Otel?.Endpoint is not null)
    {
      options.Endpoint = new Uri(logConfiguration.Otel.Endpoint);
    }
  }
}
