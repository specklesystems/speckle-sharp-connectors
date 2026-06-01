using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.TSDShared.Bindings;
using Speckle.Connectors.TSDShared.Filters;
using Speckle.Connectors.TSDShared.HostApp;

namespace Speckle.Connectors.TSDShared;

internal static class ServiceRegistration
{
  public static IServiceCollection AddTSD(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddConnectors();
    services.AddDUI<DefaultThreadContext, TSDDocumentModelStore>();
    services.AddDUIView();

    services.AddSingleton<TSDApplicationService>();
    services.AddSingleton<ITSDApplicationService>(sp => sp.GetRequiredService<TSDApplicationService>());

    // Phase 0: just boot DUI with the shared bindings. TSD model connection comes in Phase 1.
    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, TSDBasicConnectorBinding>();

    services.AddSingleton<IBinding, TSDSelectionBinding>();
    services.AddSingleton<IBinding, TSDSendBinding>();
    services.AddScoped<ISendFilter, TSDSelectionFilter>();

    return services;
  }
}
