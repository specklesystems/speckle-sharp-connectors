using Autofac;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.PlugIns;
using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Rhino.Bindings;
using Speckle.Connectors.Rhino.Filters;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.Operations.Receive;
using Speckle.Connectors.Rhino.Operations.Send;
using Speckle.Connectors.Rhino.Plugin;
using Speckle.Connectors.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Instances;
using Speckle.Connectors.Utils.Operations;
using Speckle.Core.Models.GraphTraversal;

namespace Speckle.Connectors.Rhino.DependencyInjection;

public class RhinoConnectorModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    // Register instances initialised by Rhino
    builder.AddSingleton<PlugIn>(SpeckleConnectorsRhino7Plugin.Instance);
    builder.AddSingleton<Command>(SpeckleConnectorsRhino7Command.Instance);

    builder.AddAutofac();
    builder.AddConnectorUtils();
    builder.AddDUI();
    builder.AddDUIView();

    // POC: Overwriting the SyncToMainThread to SyncToCurrentThread for Rhino!
    builder.AddSingletonInstance<ISyncToThread, SyncToCurrentThread>();

    // Register other connector specific types
    builder.AddSingleton<IRhinoPlugin, RhinoPlugin>();
    builder.AddSingleton<DocumentModelStore, RhinoDocumentStore>();
    builder.AddSingleton<IRhinoIdleManager, RhinoIdleManager>();

    // Register bindings
    builder.AddSingleton<IBinding, TestBinding>();
    builder.AddSingleton<IBinding, ConfigBinding>("connectorName", "Rhino"); // POC: Easier like this for now, should be cleaned up later
    builder.AddSingleton<IBinding, AccountBinding>();

    builder.ContainerBuilder.RegisterType<TopLevelExceptionHandlerBinding>().As<IBinding>().AsSelf().SingleInstance();
    builder.AddSingleton<ITopLevelExceptionHandler>(c =>
      c.Resolve<TopLevelExceptionHandlerBinding>().Parent.TopLevelExceptionHandler
    );

    builder
      .ContainerBuilder.RegisterType<RhinoBasicConnectorBinding>()
      .As<IBinding>()
      .As<IBasicConnectorBinding>()
      .SingleInstance();

    builder.AddSingleton<IBinding, RhinoSelectionBinding>();
    builder.AddSingleton<IBinding, RhinoSendBinding>();
    builder.AddSingleton<IBinding, RhinoReceiveBinding>();

    // binding dependencies
    builder.AddTransient<CancellationManager>();

    // register send filters
    builder.AddScoped<ISendFilter, RhinoSelectionFilter>();
    builder.AddScoped<IHostObjectBuilder, RhinoHostObjectBuilder>();

    // register send conversion cache
    builder.AddSingleton<ISendConversionCache, SendConversionCache>();

    // register send operation and dependencies
    builder.AddScoped<SendOperation<RhinoObject>>();
    builder.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    builder.AddScoped<IRootObjectBuilder<RhinoObject>, RhinoRootObjectBuilder>();
    builder.AddScoped<
      IInstanceObjectsManager<RhinoObject, List<string>>,
      InstanceObjectsManager<RhinoObject, List<string>>
    >();
    builder.AddScoped<RhinoInstanceObjectsManager>();
    builder.AddScoped<RhinoGroupManager>();
    builder.AddScoped<RhinoLayerManager>();
    builder.AddScoped<RhinoMaterialManager>();
  }
}
