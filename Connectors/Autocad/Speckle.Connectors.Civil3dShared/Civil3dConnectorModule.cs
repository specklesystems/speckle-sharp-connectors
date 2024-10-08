using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Civil3d2024;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;

namespace Speckle.Connectors.Civil3d.DependencyInjection;

public static class Civil3dConnectorModule
{
  public static void AddCivil3d(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();
    serviceCollection.LoadSend();
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, Civil3dRootObjectBuilder>();
    
    // Register vertical specific bindings
    serviceCollection.AddSingleton<IBinding, Civil3dSendBinding>();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }
}
