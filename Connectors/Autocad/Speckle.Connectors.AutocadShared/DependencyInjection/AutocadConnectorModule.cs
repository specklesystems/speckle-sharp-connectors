#if AUTOCAD
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class AutocadConnectorModule
{
  public static void AddAutocadBase(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocad();

    // Operations
    serviceCollection.LoadSend();
    serviceCollection.LoadReceive();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, ConfigBinding>(); 
  }
}
#endif
