using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Core.Common;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Logging;

namespace Speckle.Connectors.Utils;

public record ConnectorSettings(
  HostApplication HostApplication,
  string HostApplicationVersion,
  SpeckleLogConfiguration? LogConfiguration = null
);

public interface IConnector
{
  public HostApplication HostApplication { get; }
  public string HostApplicationVersion { get; }
}

public sealed class Connector : IConnector, IDisposable
{
  private readonly IDisposable _logger;

  private Connector(IDisposable logger, HostApplication hostApplication, string hostApplicationVersion)
  {
    _logger = logger;
    HostApplication = hostApplication;
    HostApplicationVersion = hostApplicationVersion;
  }

  public static Connector Start<T>(SpeckleContainerBuilder containerBuilder, ConnectorSettings connectorSettings)
  {
    // POC: not sure what this is doing...  could be messing up our Aliasing????
    AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<T>;
    var logSetup = Setup.Initialize(
      connectorSettings.HostApplication.Name,
      connectorSettings.HostApplicationVersion,
      connectorSettings.LogConfiguration
    );
    var connector = new Connector(
      logSetup,
      connectorSettings.HostApplication,
      connectorSettings.HostApplicationVersion
    );
    containerBuilder.AddSingleton<IConnector>(connector);

    connector.InternalContainer = containerBuilder.Build();
    return connector;
  }

  private SpeckleContainer? InternalContainer { get; set; }

  public HostApplication HostApplication { get; }
  public string HostApplicationVersion { get; }
  public SpeckleContainer Container => InternalContainer.NotNull();

  public void Dispose()
  {
    Container.Dispose();
    _logger.Dispose();
  }
}
