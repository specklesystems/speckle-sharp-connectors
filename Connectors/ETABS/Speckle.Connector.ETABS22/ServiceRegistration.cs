using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.ETABS22.Bindings;
using Speckle.Connector.ETABS22.Filters;
using Speckle.Connector.ETABS22.HostApp;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connector.ETABS22;

public static class ServiceRegistration
{
  public static IServiceCollection AddETABS(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddConnectorUtils();
    services.AddDUI();
    services.AddDUIView();

    services.AddSingleton<DocumentModelStore, ETABSDocumentModelStore>();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, EtabsBasicConnectorBinding>();
    services.AddSingleton<IAppIdleManager, EtabsIdleManager>();

    services.AddSingleton<IBinding, ETABSSelectionBinding>();
    services.AddSingleton<IBinding, ETABSSendBinding>();

    services.AddScoped<ISendFilter, ETABSSelectionFilter>();

    services.RegisterTopLevelExceptionHandler();

    return services;
  }
}
