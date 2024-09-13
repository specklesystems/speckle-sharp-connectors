using System.Runtime.InteropServices;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Speckle.Connectors.Logging;

public static class TracingBuilder
{
  public static IDisposable? Initialize(
    string applicationAndVersion,
    string slug,
    string connectorVersion,
    SpeckleTracing? speckleTracing
  )
  {
    var resourceBuilder = ResourceBuilder
      .CreateEmpty()
      .AddService(serviceName: LoggingActivityFactory.TRACING_SOURCE, serviceVersion: connectorVersion)
      .AddAttributes(
        new List<KeyValuePair<string, object>>
        {
          new(Consts.SERVICE_NAME, applicationAndVersion),
          new(Consts.SERVICE_SLUG, slug),
          new(Consts.OS_NAME, Environment.OSVersion.ToString()),
          new(Consts.OS_TYPE, RuntimeInformation.ProcessArchitecture.ToString()),
          new(Consts.OS_SLUG, DetermineHostOsSlug()),
          new(Consts.RUNTIME_NAME, RuntimeInformation.FrameworkDescription)
        }
      );

    return InitializeOtelTracing(speckleTracing, resourceBuilder);
  }

  private static string DetermineHostOsSlug()
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return "Windows";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      return "MacOS";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return "Linux";
    }

    return RuntimeInformation.OSDescription;
  }

  private static IDisposable? InitializeOtelTracing(SpeckleTracing? logConfiguration, ResourceBuilder resourceBuilder)
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

    tracerProviderBuilder = tracerProviderBuilder.SetResourceBuilder(resourceBuilder).SetSampler<AlwaysOnSampler>();

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
