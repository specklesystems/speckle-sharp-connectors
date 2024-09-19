using ArcGIS.Desktop.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.ArcGIS.Bindings;
using Speckle.Connectors.ArcGIS.Filters;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.Operations.Receive;
using Speckle.Connectors.ArcGis.Operations.Send;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.GraphTraversal;

// POC: This is a temp reference to root object senders to tweak CI failing after having generic interfaces into common project.
// This should go whenever it is aligned.

namespace Speckle.Connectors.ArcGIS.DependencyInjection;

public static class ArcGISConnectorModule
{
  public static void AddArcGIS(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI();
    serviceCollection.AddDUIView();

    serviceCollection.AddSingleton<DocumentModelStore, ArcGISDocumentStore>();
    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();

    serviceCollection.RegisterTopLevelExceptionHandler();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, BasicConnectorBinding>();

    serviceCollection.AddSingleton<IBinding, ArcGISSelectionBinding>();
    serviceCollection.AddSingleton<IBinding, ArcGISSendBinding>();
    serviceCollection.AddSingleton<IBinding, ArcGISReceiveBinding>();

    serviceCollection.AddTransient<ISendFilter, ArcGISSelectionFilter>();
    serviceCollection.AddScoped<IHostObjectBuilder, ArcGISHostObjectBuilder>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    // register send operation and dependencies
    serviceCollection.AddScoped<SendOperation<MapMember>>();
    serviceCollection.AddScoped<ArcGISRootObjectBuilder>();
    serviceCollection.AddScoped<IRootObjectBuilder<MapMember>, ArcGISRootObjectBuilder>();

    builder.AddScoped<LocalToGlobalConverterUtils>();

    builder.AddScoped<ArcGISColorManager>();
    builder.AddScoped<MapMembersUtils>();

    // register send conversion cache
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();

    // operation progress manager
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();
  }
}
