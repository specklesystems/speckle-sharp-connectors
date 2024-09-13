using Speckle.Connectors.Logging.Internal;

namespace Speckle.Connectors.Logging;

public static class Observability
{
  public static IDisposable? Initialize(
    string applicationAndVersion,
    string slug,
    string connectorVersion, SpeckleObservability observability)
  {
    var resourceBuilder = ResourceCreator.Create(applicationAndVersion, slug, connectorVersion);
    LogBuilder.Initialize(applicationAndVersion, connectorVersion, observability.Logging, resourceBuilder);
    return TracingBuilder.Initialize(observability.Tracing, resourceBuilder);
  }
}
