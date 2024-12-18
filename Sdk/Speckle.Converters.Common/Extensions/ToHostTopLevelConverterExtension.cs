using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Extensions;

public static class ToHostTopLevelConverterExtension
{
  public static HostResult ConvertAndLog(this ConverterResult<IToHostTopLevelConverter> converter, Base target, ILogger logger)
  {
    try
    {
      if (converter.IsSuccess)
      {
        return converter.Converter.NotNull().Convert(target);
      }

      logger.Log(
        LogLevel.Information,
        "Conversion of object {target} using {converter} was not successful",
        target.GetType(),
        converter.GetType()
      );
      return HostResult.NoConverter(converter.Message);
    }
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
      //SpeckleExceptions are expected, if a converter throws anything else, its considered an error that we should investigate and fix
      LogLevel logLevel = ex switch
      {
        SpeckleException => LogLevel.Information, //If it's too noisy, we could demote to LogLevel.Debug
        _ => LogLevel.Error
      };

      logger.Log(
        logLevel,
        ex,
        "Conversion of object {target} using {converter} was not successful",
        target.GetType(),
        converter.GetType()
      );

      return HostResult.NoConversion($"Conversion of object {target} using {converter} was not successful: " + ex.Message);
    }
  }
  public static HostResult ConvertAndLog(this IToHostTopLevelConverter converter, Base target, ILogger logger)
  {
    try
    {
      return converter.Convert(target);
    }
    catch (Exception ex)
    {
      //SpeckleExceptions are expected, if a converter throws anything else, its considered an error that we should investigate and fix
      LogLevel logLevel = ex switch
      {
        SpeckleException => LogLevel.Information, //If it's too noisy, we could demote to LogLevel.Debug
        _ => LogLevel.Error
      };

      logger.Log(
        logLevel,
        ex,
        "Conversion of object {target} using {converter} was not successful",
        target.GetType(),
        converter.GetType()
      );

      throw;
    }
  }
}
