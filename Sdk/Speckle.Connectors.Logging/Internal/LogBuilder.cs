﻿using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
    var fileVersionInfo = GetFileVersionInfo();
    var serilogLogConfiguration = new LoggerConfiguration()
      .MinimumLevel.Is(SpeckleLogLevelUtility.GetLevel(speckleLogging?.MinimumLevel ?? SpeckleLogLevel.Warning))
      .Enrich.FromLogContext()
      .Enrich.WithProperty("version", fileVersionInfo.FileVersion)
      .Enrich.WithProperty("productVersion", connectorVersion)
      .Enrich.WithProperty("hostOs", DetermineHostOsSlug())
      .Enrich.WithProperty("hostOsVersion", Environment.OSVersion)
      .Enrich.WithProperty("hostOsArchitecture", RuntimeInformation.ProcessArchitecture.ToString())
      .Enrich.WithProperty("runtime", RuntimeInformation.FrameworkDescription)
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

    foreach (var otel in speckleLogging?.Otel ?? [])
    {
      serilogLogConfiguration = InitializeOtelLogging(serilogLogConfiguration, otel, resourceBuilder);
    }
    var logger = serilogLogConfiguration.CreateLogger();

    logger
      .ForContext("hostApplication", applicationAndVersion)
      .ForContext("userApplicationDataPath", SpecklePathProvider.UserApplicationDataPath())
      .ForContext("installApplicationDataPath", SpecklePathProvider.InstallApplicationDataPath)
      .Information(
        "Initialized logger inside {hostApplication}/{productVersion}/{version} for user {id}. Path info {userApplicationDataPath} {installApplicationDataPath}."
      );

#pragma warning disable CA2000
    return new LoggerProvider(new SerilogLoggerProvider(logger));
#pragma warning restore CA2000
  }

  private static FileVersionInfo GetFileVersionInfo()
  {
    var assembly = Assembly.GetExecutingAssembly().Location;
    return FileVersionInfo.GetVersionInfo(assembly);
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
