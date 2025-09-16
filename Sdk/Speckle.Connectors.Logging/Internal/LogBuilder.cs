using System.Text;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Exceptions;

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
    var factory = LoggerFactory.Create(loggingBuilder =>
    {
      if (speckleLogging != null)
      {
        loggingBuilder.SetMinimumLevel(SpeckleLogLevelUtility.GetMicrosoftLevel(speckleLogging.MinimumLevel));
        if (speckleLogging.File is not null || speckleLogging.Console)
        {
          var serilogLogConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(SpeckleLogLevelUtility.GetSerilogLevel(speckleLogging.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails();

          if (speckleLogging.File is not null)
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

          if (speckleLogging.Console)
          {
            serilogLogConfiguration.WriteTo.Console();
          }

          var serilogLogger = serilogLogConfiguration.CreateLogger();
          if (speckleLogging.File is not null)
          {
            serilogLogger
              .ForContext("applicationAndVersion", applicationAndVersion)
              .ForContext("connectorVersion", connectorVersion)
              .ForContext("userApplicationDataPath", SpecklePathProvider.UserApplicationDataPath())
              .ForContext("installApplicationDataPath", SpecklePathProvider.InstallApplicationDataPath)
              .Information(
                "Initialized logger inside {applicationAndVersion}/{connectorVersion}. Path info {userApplicationDataPath} {installApplicationDataPath}."
              );
          }

          loggingBuilder.AddSerilog(serilogLogger);
        }
      }

      foreach (var otel in speckleLogging?.Otel ?? [])
      {
        InitializeOtelLogging(loggingBuilder, otel, resourceBuilder);
      }
    });

    return new LoggerProvider(factory);
  }

  private static void InitializeOtelLogging(
    ILoggingBuilder loggingBuilder,
    SpeckleOtelLogging speckleOtelLogging,
    ResourceBuilder resourceBuilder
  ) =>
    loggingBuilder.AddOpenTelemetry(x =>
    {
      x.AddOtlpExporter(y =>
        {
          y.Protocol = OtlpExportProtocol.HttpProtobuf;
          y.Endpoint = speckleOtelLogging.Endpoint is null
            ? throw new InvalidOperationException("Need a logging endpoint")
            : new Uri(speckleOtelLogging.Endpoint);
          var sb = new StringBuilder();
          bool appendSemicolon = false;
          foreach (var kvp in speckleOtelLogging.Headers ?? [])
          {
            sb.Append(kvp.Key).Append('=').Append(kvp.Value);
            if (appendSemicolon)
            {
              sb.Append(',');
            }
            else
            {
              appendSemicolon = true;
            }
          }
          y.Headers = sb.ToString();
        })
        .AddProcessor(new ActivityScopeLogProcessor())
        .SetResourceBuilder(resourceBuilder);
    });
}
