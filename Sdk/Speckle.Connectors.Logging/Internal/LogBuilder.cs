using OpenTelemetry.Resources;
using Serilog;
using Serilog.Exceptions;
using Serilog.Extensions.Logging;
using Serilog.Sinks.OpenTelemetry;

namespace Speckle.Connectors.Logging.Internal;

internal static class LogBuilder
{
  public static LoggerProvider Initialize(
    string applicationAndVersion,
    string connectorVersion,
    SpeckleLogging? speckleLogging,
    ResourceBuilder resourceBuilder
  )
  {
    var serilogLogConfiguration = new LoggerConfiguration()
      .MinimumLevel.Is(SpeckleLogLevelUtility.GetLevel(speckleLogging?.MinimumLevel ?? SpeckleLogLevel.Warning))
      .Enrich.FromLogContext()
      .Enrich.WithExceptionDetails();
    

    if (speckleLogging?.File is not null)
    {
      // TODO: check if we have write permissions to the file.
      var logFilePath = SpecklePathProvider.LogFolderPath(applicationAndVersion);
      logFilePath = Path.Combine(logFilePath, speckleLogging.File.Path ?? "SpeckleCoreLog.txt");
      serilogLogConfiguration = serilogLogConfiguration.WriteTo.File(
        logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 10
      );
    }

    if (speckleLogging?.Console ?? false)
    {
      serilogLogConfiguration = serilogLogConfiguration.WriteTo.Console();
    }

    if (speckleLogging?.Otel is not null)
    {
      serilogLogConfiguration = InitializeOtelLogging(serilogLogConfiguration, speckleLogging.Otel, resourceBuilder);
    }
    var logger = serilogLogConfiguration.CreateLogger();

    logger
      .ForContext("applicationAndVersion", applicationAndVersion)
      .ForContext("connectorVersion", connectorVersion)
      .ForContext("userApplicationDataPath", SpecklePathProvider.UserApplicationDataPath())
      .ForContext("installApplicationDataPath", SpecklePathProvider.InstallApplicationDataPath)
      .Information(
        "Initialized logger inside {applicationAndVersion}/{connectorVersion} for user {id}. Path info {userApplicationDataPath} {installApplicationDataPath}."
      );

#pragma warning disable CA2000
    return new LoggerProvider(new SerilogLoggerProvider(logger));
#pragma warning restore CA2000
  }

  private static LoggerConfiguration InitializeOtelLogging(
    LoggerConfiguration serilogLogConfiguration,
    SpeckleOtelLogging speckleOtelLogging,
    ResourceBuilder resourceBuilder
  ) =>
    serilogLogConfiguration.WriteTo.OpenTelemetry(o =>
    {
      o.Protocol = OtlpProtocol.HttpProtobuf;
      o.LogsEndpoint = speckleOtelLogging.Endpoint;
      o.Headers = speckleOtelLogging.Headers ?? o.Headers;
      o.ResourceAttributes = resourceBuilder.Build().Attributes.ToDictionary(x => x.Key, x => x.Value);
    });
}
