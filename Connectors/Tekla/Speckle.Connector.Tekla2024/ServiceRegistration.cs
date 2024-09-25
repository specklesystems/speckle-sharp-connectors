using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connector.Tekla2024;

public static class ServiceRegistration
{
  public static void AddTekla(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI();
    serviceCollection.AddDUIView();
  }
}
