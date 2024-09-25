#if CIVIL3D
using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class Civil3dConnectorModule
{
  public static void AddCivil3d(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddAutocadBase();
    serviceCollection.LoadSend();
  }
}
#endif
