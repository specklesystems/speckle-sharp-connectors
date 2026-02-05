using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Extensions;

public static class ToHostTopLevelConverterExtension
{
  public static object ConvertAndLog(this IToHostTopLevelConverter converter, Base target, ILogger logger)
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
        _ => LogLevel.Error,
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
