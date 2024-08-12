using ArcGIS.Desktop.Mapping;
using Autofac;
using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.ArcGIS.Bindings;
using Speckle.Connectors.ArcGIS.Filters;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.Operations.Receive;
using Speckle.Connectors.ArcGis.Operations.Send;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Instances;
using Speckle.Connectors.Utils.Operations;
using Speckle.Sdk.Models.GraphTraversal;

// POC: This is a temp reference to root object senders to tweak CI failing after having generic interfaces into common project.
// This should go whenever it is aligned.

namespace Speckle.Connectors.ArcGIS.DependencyInjection;

public class ArcGISConnectorModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    builder.AddAutofac();
    builder.AddConnectorUtils();
    builder.AddDUI();
    builder.AddDUIView();

    // POC: Overwriting the SyncToMainThread to SyncToCurrentThread for ArcGIS only!
    // On SendOperation, once we called QueuedTask, it expect to run everything on same thread.
    // builder.AddSingletonInstance<ISyncToThread, SyncToQueuedTask>();

    builder.AddSingleton<DocumentModelStore, ArcGISDocumentStore>();
    // Register bindings
    builder.AddSingleton<IBinding, TestBinding>();
    builder.AddSingleton<IBinding, ConfigBinding>("connectorName", "ArcGIS"); // POC: Easier like this for now, should be cleaned up later
    builder.AddSingleton<IBinding, AccountBinding>();

    builder.ContainerBuilder.RegisterType<TopLevelExceptionHandlerBinding>().As<IBinding>().AsSelf().SingleInstance();
    builder.AddSingleton<ITopLevelExceptionHandler>(c =>
      c.Resolve<TopLevelExceptionHandlerBinding>().Parent.TopLevelExceptionHandler
    );

    builder
      .ContainerBuilder.RegisterType<BasicConnectorBinding>()
      .As<IBinding>()
      .As<IBasicConnectorBinding>()
      .SingleInstance();

    builder.AddSingleton<IBinding, ArcGISSelectionBinding>();
    builder.AddSingleton<IBinding, ArcGISSendBinding>();
    builder.AddSingleton<IBinding, ArcGISReceiveBinding>();

    builder.AddTransient<ISendFilter, ArcGISSelectionFilter>();
    builder.AddScoped<IHostObjectBuilder, ArcGISHostObjectBuilder>();
    builder.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    // register send operation and dependencies
    builder.AddScoped<SendOperation<MapMember>>();
    builder.AddScoped<ArcGISRootObjectBuilder>();
    builder.AddScoped<IRootObjectBuilder<MapMember>, ArcGISRootObjectBuilder>();
    builder.AddSingleton<ArcGISColorManager>();

    builder.AddScoped<ILocalToGlobalUnpacker, LocalToGlobalUnpacker>();

    // register send conversion cache
    builder.AddSingleton<ISendConversionCache, SendConversionCache>();

    // operation progress manager
    builder.AddSingleton<IOperationProgressManager, OperationProgressManager>();
  }
}
