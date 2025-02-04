using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;

namespace Speckle.Connectors.Common;

public static class ContainerRegistration
{
  public static void AddConnectorUtils<T>(this IServiceCollection serviceCollection)
    where T : class, IReceiveOperation
  {
    // send operation and dependencies
    serviceCollection.AddSingleton<ICancellationManager, CancellationManager>();
    serviceCollection.AddScoped<RootObjectUnpacker>();
    serviceCollection.AddScoped<IReceiveOperation, T>();
    serviceCollection.AddSingleton<AccountService>();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());

    serviceCollection.AddTransient(typeof(ILogger<>), typeof(Logger<>));
  }
}
