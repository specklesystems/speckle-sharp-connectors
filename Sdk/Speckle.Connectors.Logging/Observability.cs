using Speckle.Connectors.Logging.Internal;
using Speckle.Connectors.Logging.Updates;

namespace Speckle.Connectors.Logging;

public static class Observability
{
  public static (LoggerProvider, IDisposable, IDisposable, ConnectorUpdateService) Initialize(
    string hpstAppExePath,
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
    var updater = new ConnectorUpdateService(
      hpstAppExePath,
      slug,
      logging.CreateLogger<ConnectorUpdateService>(),
      logging.CreateLogger<ConnectorFeedResolver>(),
      logging.CreateLogger<InnoSetupExecutor>()
    );
    return (logging, tracing, metrics, updater);
  }
}
