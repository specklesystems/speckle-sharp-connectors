#if AUTOCAD
using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class AutocadConnectorModule
{
  public static void AddAutocad(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();

    // Operations
    serviceCollection.LoadSend();
    serviceCollection.LoadReceive();
  }
}
#endif
