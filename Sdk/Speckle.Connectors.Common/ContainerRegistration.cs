using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;

namespace Speckle.Connectors.Common;

public static class ContainerRegistration
{
  public static void AddConnectors(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());

    // send operation and dependencies
    serviceCollection.AddSingleton<ICancellationManager, CancellationManager>();
    serviceCollection.AddScoped<RootObjectUnpacker>();
    serviceCollection.AddScoped<ReceiveOperation>();
    serviceCollection.AddSingleton<IAccountService, AccountService>();
    serviceCollection.AddSingleton<IMixPanelManager, MixPanelManager>();
    serviceCollection.AddSingleton<ISerializationOptions, SerializationOptions>();

    serviceCollection.AddTransient(typeof(ILogger<>), typeof(Logger<>));
  }
}
