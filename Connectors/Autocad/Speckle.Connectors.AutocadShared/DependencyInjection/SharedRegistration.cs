using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Autocad.Filters;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Receive;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.Common.ToHost;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class SharedRegistration
{
  public static void AddAutocadBase(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddConnectors();
    serviceCollection.AddDUI<DefaultThreadContext, AutocadDocumentStore>();
    serviceCollection.AddDUIView();

    // Register other connector specific types
    serviceCollection.AddTransient<TransactionContext>();
    serviceCollection.AddSingleton(new AutocadDocumentManager()); // TODO: Dependent to TransactionContext, can be moved to AutocadContext
    serviceCollection.AddSingleton<AutocadContext>();

    // Unpackers and builders
    serviceCollection.AddScoped<AutocadLayerUnpacker>();
    serviceCollection.AddScoped<AutocadLayerBaker>();

    serviceCollection.AddScoped<AutocadInstanceUnpacker>();
    serviceCollection.AddScoped<AutocadInstanceBaker>();

    serviceCollection.AddScoped<AutocadGroupUnpacker>();
    serviceCollection.AddScoped<AutocadGroupBaker>();

    serviceCollection.AddScoped<AutocadColorUnpacker>();
    serviceCollection.AddScoped<IAutocadColorBaker, AutocadColorBaker>();

    serviceCollection.AddScoped<AutocadMaterialUnpacker>();
    serviceCollection.AddScoped<IAutocadMaterialBaker, AutocadMaterialBaker>();

    serviceCollection.AddSingleton<IAppIdleManager, AutocadIdleManager>();

    // register proxy display value manager
    serviceCollection.AddScoped<IProxyDisplayValueManager, ProxyDisplayValueManager>();

    // operation progress manager
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();
    serviceCollection.AddSingleton<IBinding, AutocadSelectionBinding>();
    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, AutocadBasicConnectorBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
  }

  public static void LoadSend(this IServiceCollection serviceCollection)
  {
    // Operations
    serviceCollection.AddScoped<SendOperation<AutocadRootObject>>();

    // register send filters
    serviceCollection.AddTransient<ISendFilter, AutocadSelectionFilter>();

    // register send conversion cache
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();
    serviceCollection.AddScoped<
      IInstanceObjectsManager<AutocadRootObject, List<Entity>>,
      InstanceObjectsManager<AutocadRootObject, List<Entity>>
    >();
  }

  public static void LoadReceive(this IServiceCollection serviceCollection)
  {
    // traversal
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    // Object Builders
    serviceCollection.AddScoped<IHostObjectBuilder, AutocadHostObjectBuilder>();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, AutocadReceiveBinding>();
  }
}
