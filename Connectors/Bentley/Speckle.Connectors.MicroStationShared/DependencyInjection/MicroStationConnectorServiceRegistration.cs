using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.MicroStation.Bindings;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Connectors.MicroStation.Operations.Send.Filters;
using Speckle.Connectors.MicroStation.Plugin;

namespace Speckle.Connectors.MicroStation.DependencyInjection;

public static class MicroStationConnectorServiceRegistration
{
  public static void AddMicroStation(this IServiceCollection serviceCollection)
  {
    // Core DUI3 infrastructure
    serviceCollection.AddConnectors();
    serviceCollection.AddDUI<DefaultThreadContext, MicroStationDocumentModelStore>();
    serviceCollection.AddDUIView();

    // Register the MicroStation COM Application object so selection/idle code can receive it via DI
    serviceCollection.AddSingleton<Application>(_ => MsApp.Instance);

    // Standard bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();

    // MicroStation-specific bindings
    serviceCollection.AddSingleton<IBinding, MicroStationSelectionBinding>();
    serviceCollection.AddSingleton<IBinding, MicroStationSendBinding>();
    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, MicroStationBasicConnectorBinding>();

    // Idle / threading
    serviceCollection.AddSingleton<IAppIdleManager, MicroStationIdleManager>();

    // Operation progress
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();

    // Send pipeline — occurrence-tagged managed elements flow through gatherer → unpacker →
    // builder → the converter's DisplayValueExtractor.
    serviceCollection.AddSingleton<MicroStationElementGatherer>();
    serviceCollection.AddScoped<IRootObjectBuilder<MicroStationRootObject>, MicroStationRootObjectBuilder>();
    serviceCollection.AddScoped<SendOperation<MicroStationRootObject>>();
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();

    // Shared-cell instancing (the AutoCAD block pattern)
    serviceCollection.AddScoped<IInstanceUnpacker<MicroStationRootObject>, MicroStationInstanceUnpacker>();
    serviceCollection.AddScoped<
      IInstanceObjectsManager<MicroStationRootObject, List<MicroStationRootObject>>,
      InstanceObjectsManager<MicroStationRootObject, List<MicroStationRootObject>>
    >();

    // Send filters
    serviceCollection.AddScoped<ISendFilter, MicroStationEverythingFilter>();
    serviceCollection.AddScoped<ISendFilter, MicroStationSelectionFilter>();
    serviceCollection.AddScoped<ISendFilter, MicroStationLevelFilter>();

    // Document state and events
    serviceCollection.AddSingleton<MicroStationDocumentModelStore>();
    serviceCollection.AddSingleton<DocumentModelStore>(sp => sp.GetRequiredService<MicroStationDocumentModelStore>());
    serviceCollection.AddSingleton<MicroStationDocumentEvents>();
  }
}
