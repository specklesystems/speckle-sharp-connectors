using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.ETABS22.Bindings;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connector.ETABS22;

public static class ServiceRegistration
{
  public static IServiceCollection AddETABS(this IServiceCollection services)
  {
    services.AddConnectorUtils();
    services.AddDUI();
    services.AddDUIView();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, EtabsBasicConnectorBinding>();
    services.AddSingleton<IAppIdleManager, EtabsIdleManager>();

    return services;
  }
}
