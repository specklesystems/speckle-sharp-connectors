using Microsoft.Extensions.DependencyInjection;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.PlugIns;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Threading;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Rhino.Bindings;
using Speckle.Connectors.Rhino.Filters;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.Operations.Receive;
using Speckle.Connectors.Rhino.Operations.Send;
using Speckle.Connectors.Rhino.Plugin;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Rhino.DependencyInjection;

public static class ServiceRegistration
{
  public static void AddRhino(this IServiceCollection serviceCollection)
  {
    // Register instances initialised by Rhino
    serviceCollection.AddSingleton<PlugIn>(SpeckleConnectorsRhinoPlugin.Instance);
    serviceCollection.AddSingleton<Command>(SpeckleConnectorsRhinoCommand.Instance);

    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI<ThreadContext>();
    serviceCollection.AddDUIView();

    // Register other connector specific types
    serviceCollection.AddSingleton<IRhinoPlugin, RhinoPlugin>();
    serviceCollection.AddSingleton<DocumentModelStore, RhinoDocumentStore>();
    serviceCollection.AddSingleton<IAppIdleManager, RhinoIdleManager>();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>(); // POC: Easier like this for now, should be cleaned up later
    serviceCollection.AddSingleton<IBinding, AccountBinding>();

    serviceCollection.RegisterTopLevelExceptionHandler();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, RhinoBasicConnectorBinding>();

    serviceCollection.AddSingleton<IBinding, RhinoSelectionBinding>();
    serviceCollection.AddSingleton<IBinding, RhinoSendBinding>();
    serviceCollection.AddSingleton<IBinding, RhinoReceiveBinding>();

    // binding dependencies
    serviceCollection.AddTransient<CancellationManager>();

    // register send filters
    serviceCollection.AddScoped<ISendFilter, RhinoSelectionFilter>();
    serviceCollection.AddScoped<IHostObjectBuilder, RhinoHostObjectBuilder>();

    // register send conversion cache
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();

    // register send operation and dependencies
    serviceCollection.AddScoped<SendOperation<RhinoObject>>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    serviceCollection.AddScoped<IRootObjectBuilder<RhinoObject>, RhinoRootObjectBuilder>();
    serviceCollection.AddScoped<
      IInstanceObjectsManager<RhinoObject, List<string>>,
      InstanceObjectsManager<RhinoObject, List<string>>
    >();

    // Register unpackers and bakers
    serviceCollection.AddScoped<RhinoLayerUnpacker>();
    serviceCollection.AddScoped<RhinoLayerBaker>();

    serviceCollection.AddScoped<RhinoInstanceBaker>();
    serviceCollection.AddScoped<RhinoInstanceUnpacker>();

    serviceCollection.AddScoped<RhinoGroupBaker>();
    serviceCollection.AddScoped<RhinoGroupUnpacker>();

    serviceCollection.AddScoped<RhinoMaterialBaker>();
    serviceCollection.AddScoped<RhinoMaterialUnpacker>();

    serviceCollection.AddScoped<RhinoColorBaker>();
    serviceCollection.AddScoped<RhinoColorUnpacker>();

    // operation progress manager
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();
  }
}
