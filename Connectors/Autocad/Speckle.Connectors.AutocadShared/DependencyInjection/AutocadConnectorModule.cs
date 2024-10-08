#if AUTOCAD
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.DUI.Bindings;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class AutocadConnectorModule
{
  public static void AddAutocad(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();

    // Send
    serviceCollection.LoadSend();
    serviceCollection.AddScoped<IRootObjectBuilder<AutocadRootObject>, AutocadRootObjectBuilder>();

    // Receive
    serviceCollection.LoadReceive();
    
    // Register vertical specific bindings
    serviceCollection.AddSingleton<IBinding, AutocadSendBinding>();
  }
}
#endif
