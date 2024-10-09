using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
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

    // register civil specific send classes
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, Civil3dRootObjectBuilder>();
    serviceCollection.AddSingleton<IBinding, Civil3dSendBinding>();

    // automatically detects the Class:IClass interface pattern to register all generated interfaces
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }
}
