using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Plant3dShared.Bindings;
using Speckle.Connectors.Plant3dShared.HostApp;
using Speckle.Connectors.Plant3dShared.Operations.Receive;
using Speckle.Connectors.Plant3dShared.Operations.Send;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Converters.Plant3dShared.ToSpeckle;
using Speckle.Sdk;

namespace Speckle.Connectors.Plant3dShared.DependencyInjection;

public static class Plant3dConnectorModule
{
  public static void AddPlant3d(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();

    // add send
    serviceCollection.LoadSend();
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, Plant3dRootObjectBuilder>();
    serviceCollection.AddSingleton<IBinding, Plant3dSendBinding>();

    // add receive
    serviceCollection.LoadReceive();
    serviceCollection.AddScoped<IHostObjectBuilder, Plant3dHostObjectBuilder>();
    serviceCollection.AddSingleton<IBinding, Plant3dReceiveBinding>();

    // additional classes
    serviceCollection.AddScoped<PropertySetDefinitionHandler>();
    serviceCollection.AddScoped<PropertySetBaker>();

    // automatically detects the Class:IClass interface pattern to register all generated interfaces
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }
}
