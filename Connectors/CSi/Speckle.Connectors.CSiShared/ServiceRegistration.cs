using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
<<<<<<< HEAD
using Speckle.Connectors.Common.Threading;
=======
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
>>>>>>> dev
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
  public static IServiceCollection AddCSi(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();
    services.AddSingleton<ICSiApplicationService, CSiApplicationService>();

    services.AddConnectorUtils();
<<<<<<< HEAD
    services.AddDUI<DefaultThreadContext, CSiSharedDocumentModelStore>();
=======
    services.AddDUI<CSiDocumentModelStore>();
>>>>>>> dev
    services.AddDUIView();

    services.AddSingleton<DocumentModelStore, CSiDocumentModelStore>();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, CSiSharedBasicConnectorBinding>();
    services.AddSingleton<IAppIdleManager, CSiIdleManager>();

    services.AddSingleton<IBinding, CSiSharedSelectionBinding>();
    services.AddSingleton<IBinding, CSiSharedSendBinding>();

    services.AddScoped<ISendFilter, CSiSharedSelectionFilter>();
    services.AddScoped<CSiSendCollectionManager>();
    services.AddScoped<IRootObjectBuilder<ICSiWrapper>, CSiRootObjectBuilder>();
    services.AddScoped<SendOperation<ICSiWrapper>>();

    services.RegisterTopLevelExceptionHandler();

    return services;
  }
}
