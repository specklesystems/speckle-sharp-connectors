using Autodesk.AutoCAD.DatabaseServices;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Autocad.Filters;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Receive;
using Speckle.Connectors.Autocad.Operations.Send;
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
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Autocad.DependencyInjection;

public static class SharedRegistration
{
  public static void Load(SpeckleContainerBuilder builder, SpeckleConfiguration configuration)
  {
    builder.AddAutofac();
    builder.AddConnectorUtils(configuration);
    builder.AddDUI();
    builder.AddDUIView();

    // Register other connector specific types
    builder.AddTransient<TransactionContext>();
    builder.AddSingleton(new AutocadDocumentManager()); // TODO: Dependent to TransactionContext, can be moved to AutocadContext
    builder.AddSingleton<DocumentModelStore, AutocadDocumentStore>();
    builder.AddSingleton<AutocadContext>();

    // Unpackers and builders
    builder.AddScoped<AutocadLayerUnpacker>();
    builder.AddScoped<AutocadLayerBaker>();

    builder.AddScoped<AutocadInstanceUnpacker>();
    builder.AddScoped<AutocadInstanceBaker>();

    builder.AddScoped<AutocadGroupUnpacker>();
    builder.AddScoped<AutocadGroupBaker>();

    builder.AddScoped<AutocadColorUnpacker>();
    builder.AddScoped<AutocadColorBaker>();

    builder.AddScoped<AutocadMaterialUnpacker>();
    builder.AddScoped<AutocadMaterialBaker>();

    builder.AddSingleton<IAutocadIdleManager, AutocadIdleManager>();
    builder.AddSingleton<IHostToSpeckleUnitConverter<UnitsValue>>();

    // operation progress manager
    builder.AddSingleton<IOperationProgressManager, OperationProgressManager>();

    // Register bindings
    builder.AddSingleton<IBinding, TestBinding>();
    builder.AddSingleton<IBinding, AccountBinding>();
    builder.AddSingleton<IBinding, AutocadSelectionBinding>();
    builder
      .ContainerBuilder.RegisterType<AutocadBasicConnectorBinding>()
      .As<IBinding>()
      .As<IBasicConnectorBinding>()
      .SingleInstance();

    //Top Level ExceptionHandler
    builder.ContainerBuilder.RegisterType<TopLevelExceptionHandlerBinding>().As<IBinding>().AsSelf().SingleInstance();
    builder.AddSingleton<ITopLevelExceptionHandler>(c =>
      c.Resolve<TopLevelExceptionHandlerBinding>().Parent.TopLevelExceptionHandler
    );
  }

  public static void LoadSend(SpeckleContainerBuilder builder)
  {
    // Operations
    builder.AddScoped<SendOperation<AutocadRootObject>>();

    // Object Builders
    builder.AddScoped<IRootObjectBuilder<AutocadRootObject>, AutocadRootObjectBuilder>();

    // Register bindings
    builder.AddSingleton<IBinding, AutocadSendBinding>();

    // register send filters
    builder.AddTransient<ISendFilter, AutocadSelectionFilter>();

    // register send conversion cache
    builder.AddSingleton<ISendConversionCache, SendConversionCache>();
    builder.AddScoped<
      IInstanceObjectsManager<AutocadRootObject, List<Entity>>,
      InstanceObjectsManager<AutocadRootObject, List<Entity>>
    >();
  }

  public static void LoadReceive(SpeckleContainerBuilder builder)
  {
    // traversal
    builder.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    // Object Builders
    builder.AddScoped<IHostObjectBuilder, AutocadHostObjectBuilder>();

    // Register bindings
    builder.AddSingleton<IBinding, AutocadReceiveBinding>();
  }
}
