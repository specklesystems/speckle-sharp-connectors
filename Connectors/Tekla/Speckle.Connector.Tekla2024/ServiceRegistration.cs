using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Tekla2024.Bindings;
using Speckle.Connector.Tekla2024.Filters;
using Speckle.Connector.Tekla2024.HostApp;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Tekla.Structures.Model;

namespace Speckle.Connector.Tekla2024;

public static class ServiceRegistration
{
  public static void AddTekla(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI();
    serviceCollection.AddDUIView();

    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();

    serviceCollection.AddSingleton<DocumentModelStore, TeklaDocumentModelStore>();

    serviceCollection.RegisterTopLevelExceptionHandler();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, TeklaBasicConnectorBinding>();

    serviceCollection.AddSingleton<IBinding, TeklaSendBinding>();
    serviceCollection.AddSingleton<IBinding, TeklaSelectionBinding>();
    serviceCollection.AddSingleton<IAppIdleManager, TeklaIdleManager>();

    serviceCollection.AddScoped<ISendFilter, TeklaSelectionFilter>();
    serviceCollection.AddSingleton(new Tekla.Structures.Model.Events());
    serviceCollection.AddSingleton(new Tekla.Structures.Model.UI.ModelObjectSelector());
  }
}
