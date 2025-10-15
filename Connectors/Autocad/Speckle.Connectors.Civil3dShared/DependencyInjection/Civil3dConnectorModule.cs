using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Civil3dShared.Bindings;
using Speckle.Connectors.Civil3dShared.Operations.Send;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Civil3dShared.ToSpeckle;
using Speckle.Sdk;

namespace Speckle.Connectors.Civil3dShared.DependencyInjection;

public static class Civil3dConnectorModule
{
  public static void AddCivil3d(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();

    // add send
    serviceCollection.LoadSend();
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, Civil3dRootObjectBuilder>();
    serviceCollection.AddSingleton<IBinding, Civil3dSendBinding>();

    // add receive
    serviceCollection.LoadReceive();
    serviceCollection.AddSingleton<IBinding, Civil3dReceiveBinding>();

    // additional classes
    serviceCollection.AddScoped<PropertySetDefinitionHandler>();
    serviceCollection.AddScoped<PropertySetBaker>();

    // automatically detects the Class:IClass interface pattern to register all generated interfaces
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }
}
