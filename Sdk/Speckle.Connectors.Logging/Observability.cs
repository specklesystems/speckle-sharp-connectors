using Speckle.Connectors.Logging.Internal;

namespace Speckle.Connectors.Logging;

public static class Observability
{
  public static (ISpeckleLogger, IDisposable?) Initialize(
    string applicationAndVersion,
    string slug,
    string connectorVersion,
    SpeckleObservability observability
  )
  {
    var resourceBuilder = ResourceCreator.Create(applicationAndVersion, slug, connectorVersion);
    var logging = LogBuilder.Initialize(
      applicationAndVersion,
      connectorVersion,
      observability.Logging,
      resourceBuilder
    );
    var tracing = TracingBuilder.Initialize(observability.Tracing, resourceBuilder);
    return (logging, tracing);
  }
}
