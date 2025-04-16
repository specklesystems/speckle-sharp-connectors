using Speckle.Connectors.Logging.Internal;
using Speckle.Connectors.Logging.Updates;

namespace Speckle.Connectors.Logging;

public static class Observability
{
  public static (LoggerProvider, IDisposable, IDisposable, ConnectorUpdateService) Initialize(
    string name,
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
    var metrics = MetricsBuilder.Initialize(observability.Metrics, resourceBuilder);
    var updater = new ConnectorUpdateService(name, slug, logging.CreateLogger<ConnectorUpdateService>(), logging.CreateLogger<ConnectorFeedResolver>());
    return (logging, tracing, metrics, updater);
  }
}
