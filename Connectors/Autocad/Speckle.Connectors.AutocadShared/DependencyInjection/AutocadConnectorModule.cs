#if AUTOCAD
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class AutocadConnectorModule
{
  public static void AddAutocad(this IServiceCollection serviceCollection, HostAppVersion version)
  {
    serviceCollection.AddAutocadBase(HostApplications.AutoCAD, version);

    // Send
    serviceCollection.LoadSend();
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, AutocadRootObjectBuilder>();

    // Receive
    serviceCollection.LoadReceive();

    // Register vertical specific bindings
    serviceCollection.AddSingleton<IBinding, AutocadSendBinding>();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }
}
#endif
