using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connector.Navisworks.Filters;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Operations.Send;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connector.Navisworks.DependencyInjection;

public static class NavisworksServiceRegistration
{
  public static void AddNavisworks(this IServiceCollection serviceCollection)
  {
    // Register Core functionality
    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI<NavisworksDocumentModelStore>();
    serviceCollection.AddDUIView();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();
    serviceCollection.AddSingleton<IBinding, NavisworksSelectionBinding>();
    serviceCollection.AddSingleton<IBinding, NavisworksSendBinding>();

    // Register Navisworks specific binding
    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, NavisworksBasicConnectorBinding>();
    serviceCollection.AddSingleton<INavisworksConversionSettingsFactory, NavisworksConversionSettingsFactory>();

    // Conversion settings
    serviceCollection.AddScoped<
      IConverterSettingsStore<NavisworksConversionSettings>,
      ConverterSettingsStore<NavisworksConversionSettings>
    >();

    // Sending operations
    serviceCollection.AddScoped<IRootObjectBuilder<NAV.ModelItem>, NavisworksRootObjectBuilder>();
    serviceCollection.AddScoped<SendOperation<NAV.ModelItem>>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    // Register Intercom/interop
    serviceCollection.RegisterTopLevelExceptionHandler();
    serviceCollection.AddTransient<CancellationManager>();
    serviceCollection.AddSingleton<IAppIdleManager, NavisworksIdleManager>();
    serviceCollection.AddSingleton<DocumentModelStore, NavisworksDocumentModelStore>();
    serviceCollection.AddSingleton<NavisworksDocumentEvents>();

    // register filters
    serviceCollection.AddScoped<ISendFilter, NavisworksSelectionFilter>();
  }
}
