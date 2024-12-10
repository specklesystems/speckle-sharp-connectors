using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Grasshopper8;

public static class ServiceRegistration
{
  public static IServiceCollection AddGrasshopper(this IServiceCollection services)
  {
    services.AddTransient<IHostObjectBuilder, GrasshopperHostObjectBuilder>();
    services.AddTransient<GrasshopperReceiveOperation>();
    services.AddTransient<GrasshopperSendOperation>();
    services.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    services.AddScoped<RootObjectUnpacker>();

    services.AddTransient<TraversalContextUnpacker>();
    services.AddTransient<AccountManager>();

    return services;
  }
}
