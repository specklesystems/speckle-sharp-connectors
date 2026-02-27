using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.Bindings;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Operations.Send;
using Speckle.Connector.Navisworks.Operations.Send.Filters;
using Speckle.Connector.Navisworks.Operations.Send.Settings;
using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converter.Navisworks.Constants.Registers;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connector.Navisworks.DependencyInjection;

public static class NavisworksConnectorServiceRegistration
{
  public static void AddNavisworks(this IServiceCollection serviceCollection)
  {
    // Register Core functionality
    serviceCollection.AddConnectors();
    serviceCollection.AddDUI<DefaultThreadContext, NavisworksDocumentModelStore>();
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
    serviceCollection.AddSingleton<ToSpeckleSettingsManagerNavisworks>();
    serviceCollection.AddScoped<
      IConverterSettingsStore<NavisworksConversionSettings>,
      ConverterSettingsStore<NavisworksConversionSettings>
    >();

    serviceCollection.AddScoped<NavisworksMaterialUnpacker>();
    serviceCollection.AddScoped<NavisworksColorUnpacker>();

    serviceCollection.AddSingleton<IAppIdleManager, NavisworksIdleManager>();

    // Sending operations
    serviceCollection.AddScoped<IRootObjectBuilder<NAV.ModelItem>, NavisworksRootObjectBuilder>();
    serviceCollection.AddScoped<
      IRootContinuousTraversalBuilder<NAV.ModelItem>,
      NavisworksContinuousTraversalBuilder
    >();
    serviceCollection.AddScoped<SendOperation<NAV.ModelItem>>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();

    // Registers and caches
    serviceCollection.AddScoped<IInstanceFragmentRegistry, InstanceFragmentRegistry>();

    // Register Intercom/interop
    serviceCollection.AddSingleton<NavisworksDocumentModelStore>();
    serviceCollection.AddSingleton<DocumentModelStore>(sp => sp.GetRequiredService<NavisworksDocumentModelStore>());
    serviceCollection.AddSingleton<NavisworksDocumentEvents>();

    // register filters
    serviceCollection.AddScoped<ISendFilter, NavisworksSelectionFilter>();
    serviceCollection.AddScoped<ISendFilter, NavisworksSavedSetsFilter>();
    serviceCollection.AddScoped<ISendFilter, NavisworksSavedViewsFilter>();
    serviceCollection.AddScoped<
      Converter.Navisworks.Services.IElementSelectionService,
      ConnectorElementSelectionService
    >();
  }
}
