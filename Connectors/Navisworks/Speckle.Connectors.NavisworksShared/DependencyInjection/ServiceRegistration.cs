using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connector.Navisworks.Filters;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connector.Navisworks.DependencyInjection;

public static class ServiceRegistration
{
  public static void AddNavisworks(this IServiceCollection serviceCollection)
  {
    // Register Core functionality
    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI();
    serviceCollection.AddDUIView();

    // Register Intercom/interop
    serviceCollection.RegisterTopLevelExceptionHandler();
    serviceCollection.AddTransient<CancellationManager>();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();

    serviceCollection.AddSingleton<IBasicConnectorBinding, BasicConnectorBinding>();

    // Register Navisworks specific bindings
    serviceCollection.AddSingleton<DocumentModelStore, NavisworksDocumentStore>();
    serviceCollection.AddSingleton<IBinding, NavisworksSelectionBinding>();
    serviceCollection.AddSingleton<IBinding, NavisworksSendBinding>();
    serviceCollection.AddScoped<ISendFilter, NavisworksSelectionFilter>();

    // binding dependencies
    serviceCollection.AddTransient<CancellationManager>();

    // register send filters
    serviceCollection.AddScoped<ISendFilter, NavisworksSelectionFilter>();
  }
}
