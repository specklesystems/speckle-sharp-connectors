using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Logging;
using Speckle.Connectors.Utils.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Utils;

public static class Connector
{
  public static readonly string TabName = "Speckle";
  public static readonly string TabTitle = "Speckle (Beta)";

  public static HostAppVersion Version { get; private set; } = HostAppVersion.v3;
  public static string VersionString { get; private set; } = string.Empty;
  public static string Name => HostApp.Name;
  public static string Slug => HostApp.Slug;

  public static HostApplication HostApp { get; private set; }

  public static IDisposable? Initialize(
    HostApplication application,
    HostAppVersion version,
    SpeckleContainerBuilder builder
  )
  {
    Version = version;
    VersionString = HostApplications.GetVersion(version);
    HostApp = application;
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);

    var (logging, tracing) = Observability.Initialize(
      VersionString,
      Slug,
      Assembly.GetExecutingAssembly().GetVersion(),
      new(
        new SpeckleLogging(Console: true, Otel: null, MinimumLevel: SpeckleLogLevel.Warning),
        new SpeckleTracing(Console: false, Otel: null)
      )
    );

    IServiceCollection serviceCollection = new ServiceCollection();
    serviceCollection.AddLogging(x => x.AddProvider(new SpeckleLogProvider(logging)));
    serviceCollection.AddSpeckleSdk(application, version);
    serviceCollection.AddSingleton<Speckle.Sdk.Logging.ISdkActivityFactory, ConnectorActivityFactory>();
    //do this last
    builder.ContainerBuilder.Populate(serviceCollection);
    return tracing;
  }
}
