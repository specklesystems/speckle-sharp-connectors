using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Sdk;

namespace Speckle.Connectors.Common.Extensions;

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

public static class CollectionExtensions
{
  public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
  {
    foreach (var item in items)
    {
      collection.Add(item);
    }
  }

#if NETSTANDARD2_0
  public static HashSet<T> ToHashSet<T>(this IEnumerable<T> items)
  {
    var set = new HashSet<T>(items);
    return set;
  }
#endif
}
