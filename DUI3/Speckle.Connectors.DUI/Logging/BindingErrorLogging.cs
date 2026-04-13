using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Logging;

/// <remarks>
/// These helper functions are designed because we are handling all <see cref="Exception"/>s using the model card
/// But we still would like to discriminate the severity via logging.
/// If we ever decide to be more specific with our exception catching within bindings (to allow unexpected exceptions to bubble up to the top level)
/// then these functions likely won't be useful.
/// </remarks>
public static class HandledModelCardErrors
{
  public static void LogModelCardHandledError<T>(this ILogger<T> logger, Exception ex)
  {
    LogLevel level = ex switch
    {
      SpeckleException => LogLevel.Warning,
      _ => LogLevel.Error,
    };

    logger.Log(level, ex, "{bindingType} operation was not successful", typeof(T));
  }
}
