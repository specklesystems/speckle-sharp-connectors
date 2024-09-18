﻿using Microsoft.Extensions.Logging;
using Speckle.Connectors.Utils.Builders;
using Speckle.Sdk;

namespace Speckle.Connectors.Utils.Extensions;

public static class RootObjectBuilderExtensions
{
  public static void LogSendConversionError<T>(
    this ILogger<IRootObjectBuilder<T>> logger,
    Exception ex,
    string objectType
  )
  {
    LogLevel logLevel = ex switch
    {
      SpeckleException => LogLevel.Information, //If it's too noisy, we could demote to LogLevel.Debug
      _ => LogLevel.Error
    };

    logger.Log(logLevel, ex, "Conversion of object {objectType} was not successful", objectType);
  }
}
