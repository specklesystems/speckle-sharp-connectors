using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Common.Threading;
using Speckle.Sdk;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Common;

public static class ContainerRegistration
{
  public static void AddConnectors<THostObjectBuilder, TThreadContext>(
    this IServiceCollection serviceCollection,
    Application application,
    HostAppVersion applicationVersion,
    string? speckleVersion = null,
    IEnumerable<Assembly>? assemblies = null
  )
    where THostObjectBuilder : class, IHostObjectBuilder
    where TThreadContext : IThreadContext, new()
  {
    serviceCollection.AddScoped<IHostObjectBuilder, THostObjectBuilder>();
    serviceCollection.AddConnectors<TThreadContext>(application, applicationVersion, speckleVersion, assemblies);
  }

  public static void AddConnectors<TThreadContext>(
    this IServiceCollection serviceCollection,
    Application application,
    HostAppVersion applicationVersion,
    string? speckleVersion = null,
    IEnumerable<Assembly>? assemblies = null
  )
    where TThreadContext : IThreadContext, new()
  {
    serviceCollection.AddSpeckleSdk(
      application,
      HostApplications.GetVersion(applicationVersion),
      speckleVersion,
      assemblies
    );
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());

    // send operation and dependencies
    serviceCollection.AddSingleton<ICancellationManager, CancellationManager>();
    serviceCollection.AddScoped<RootObjectUnpacker>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    serviceCollection.AddScoped<ReceiveOperation>();
    serviceCollection.AddSingleton<IAccountService, AccountService>();
    serviceCollection.AddSingleton<IMixPanelManager, MixPanelManager>();
    serviceCollection.AddSingleton<IThreadContext>(new TThreadContext());

    serviceCollection.AddTransient(typeof(ILogger<>), typeof(Logger<>));
  }
}
