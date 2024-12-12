using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.Bindings;
using Speckle.Connectors.CSiShared.Builders;
using Speckle.Connectors.CSiShared.Filters;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.CSiShared;

namespace Speckle.Connectors.CSiShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddCsi(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();
    services.AddSingleton<ICsiApplicationService, CsiApplicationService>();

    services.AddConnectorUtils();
    services.AddDUI<CsiDocumentModelStore>();
    services.AddDUIView();

    services.AddSingleton<DocumentModelStore, CsiDocumentModelStore>();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, CsiSharedBasicConnectorBinding>();
    services.AddSingleton<IAppIdleManager, CsiIdleManager>();

    services.AddSingleton<IBinding, CsiSharedSelectionBinding>();
    services.AddSingleton<IBinding, CsiSharedSendBinding>();

    services.AddScoped<ISendFilter, CsiSharedSelectionFilter>();
    services.AddScoped<CsiSendCollectionManager>();
    services.AddScoped<IRootObjectBuilder<IReadOnlyList<ICsiWrapper>>, CsiRootObjectBuilder>();
    services.AddScoped<SendOperation<ICsiWrapper>>();

    services.RegisterTopLevelExceptionHandler();

    return services;
  }
}
