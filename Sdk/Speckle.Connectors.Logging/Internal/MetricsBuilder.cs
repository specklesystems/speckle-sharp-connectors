using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Speckle.Connectors.Logging.Internal;

internal static class MetricsBuilder
{
  public static IDisposable Initialize(SpeckleMetrics? metricsConfiguration, ResourceBuilder resourceBuilder)
  {
    var metricsProviderBuilder = OpenTelemetry
      .Sdk.CreateMeterProviderBuilder()
      .AddMeter(Consts.TRACING_SOURCE);
    foreach(var metrics in metricsConfiguration?.Otel ?? [])
    {
      metricsProviderBuilder = metricsProviderBuilder.AddOtlpExporter(x => ProcessOptions(metrics, x));
    }

    if (metricsConfiguration?.Console ?? false)
    {
      metricsProviderBuilder = metricsProviderBuilder.AddConsoleExporter();
    }

    metricsProviderBuilder = metricsProviderBuilder.SetResourceBuilder(resourceBuilder);

    return metricsProviderBuilder.Build();
  }

  private static void ProcessOptions(SpeckleOtelMetrics metrics, OtlpExporterOptions options)
  {
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    var headers = string.Join(",", metrics.Headers?.Select(x => x.Key + "=" + x.Value) ?? []);
    if (headers.Length != 0)
    {
      options.Headers = headers;
    }

    if (metrics.Endpoint is not null)
    {
      options.Endpoint = new Uri(metrics.Endpoint);
    }
  }
}
