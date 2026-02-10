using Microsoft.Extensions.DependencyInjection;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.PlugIns;
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
using Speckle.Connectors.Rhino.Bindings;
using Speckle.Connectors.Rhino.Filters;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.HostApp.Properties;
using Speckle.Connectors.Rhino.Mapper.Revit;
using Speckle.Connectors.Rhino.Operations.Receive;
using Speckle.Connectors.Rhino.Operations.Send;
using Speckle.Connectors.Rhino.Operations.Send.Settings;
using Speckle.Connectors.Rhino.Plugin;
using Speckle.Converters.Common.ToHost;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Rhino.DependencyInjection;

public static class ServiceRegistration
{
  public static void AddRhino(this IServiceCollection serviceCollection, bool isConnector)
  {
    if (isConnector)
    {
      // Register instances initialised by Rhino
      serviceCollection.AddSingleton<PlugIn>(SpeckleConnectorsRhinoPlugin.Instance);
      serviceCollection.AddSingleton<Command>(SpeckleConnectorsRhinoCommand.Instance);
      serviceCollection.AddDUI<DefaultThreadContext, RhinoDocumentStore>();
      serviceCollection.AddDUIView();
    }

    serviceCollection.AddConnectors();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>(); // POC: Easier like this for now, should be cleaned up later
    serviceCollection.AddSingleton<IBinding, AccountBinding>();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, RhinoBasicConnectorBinding>();

    serviceCollection.AddSingleton<IBinding, RhinoSelectionBinding>();
    serviceCollection.AddSingleton<IBinding, RhinoSendBinding>();
    serviceCollection.AddSingleton<IBinding, RhinoReceiveBinding>();
    serviceCollection.AddSingleton<IBinding, RhinoMapperBinding>();

    // register send filters
    serviceCollection.AddScoped<ISendFilter, RhinoSelectionFilter>();
    serviceCollection.AddScoped<IHostObjectBuilder, RhinoHostObjectBuilder>();

    // register send settings
    serviceCollection.AddScoped<ToSpeckleSettingsManager>();

    // register send conversion cache
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();
    serviceCollection.AddSingleton<IAppIdleManager, RhinoIdleManager>();

    // register send operation and dependencies
    serviceCollection.AddScoped<SendOperation<RhinoObject>>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    serviceCollection.AddScoped<IRootObjectBuilder<RhinoObject>, RhinoRootObjectBuilder>();
    serviceCollection.AddScoped<IRootContinuousTraversalBuilder<RhinoObject>, RhinoContinuousTraversalBuilder>();
    serviceCollection.AddScoped<
      IInstanceObjectsManager<RhinoObject, List<string>>,
      InstanceObjectsManager<RhinoObject, List<string>>
    >();

    // register unpackers and bakers
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

    serviceCollection.AddScoped<RhinoViewUnpacker>();
    serviceCollection.AddScoped<RhinoViewBaker>();

    serviceCollection.AddScoped<PropertiesExtractor>();
    serviceCollection.AddScoped<RevitMappingResolver>();

    // handling proxified display values
    serviceCollection.AddScoped<IDataObjectInstanceRegistry, DataObjectInstanceRegistry>();
    serviceCollection.AddScoped<DataObjectInstanceGrouper>();

    // register helpers
    serviceCollection.AddScoped<RhinoLayerHelper>();
    serviceCollection.AddScoped<RhinoObjectHelper>();

    // operation progress manager
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();
  }
}
