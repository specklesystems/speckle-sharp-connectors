#if AUTOCAD
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common.Builders;

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
  }
}
#endif
