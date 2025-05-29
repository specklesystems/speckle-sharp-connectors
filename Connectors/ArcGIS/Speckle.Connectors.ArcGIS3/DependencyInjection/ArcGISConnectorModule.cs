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
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.Common;

// POC: This is a temp reference to root object senders to tweak CI failing after having generic interfaces into common project.
// This should go whenever it is aligned.

namespace Speckle.Connectors.ArcGIS.DependencyInjection;

public static class ArcGISConnectorModule
{
  public static void AddArcGIS(this IServiceCollection serviceCollection, HostAppVersion version)
  {
    serviceCollection.AddDUISendOnly<ArcGISDocumentStore, DefaultThreadContext>(HostApplications.ArcGIS, version);
    serviceCollection.AddDUIView();

    // Register bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();
    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, BasicConnectorBinding>();

    // register send operation and dependencies
    serviceCollection.AddSingleton<IBinding, ArcGISSendBinding>();
    serviceCollection.AddScoped<SendOperation<MapMember>>();
    serviceCollection.AddSingleton<IBinding, ArcGISSelectionBinding>();
    serviceCollection.AddTransient<ISendFilter, ArcGISSelectionFilter>();
    serviceCollection.AddScoped<ArcGISRootObjectBuilder>();
    serviceCollection.AddScoped<IRootObjectBuilder<MapMember>, ArcGISRootObjectBuilder>();
    serviceCollection.AddScoped<ArcGISLayerUnpacker>();
    serviceCollection.AddScoped<ArcGISColorUnpacker>();
    // register send conversion cache
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();

    // register receive operation and dependencies
    // serviceCollection.AddSingleton<IBinding, ArcGISReceiveBinding>(); // POC: disabled until receive code is refactored
    serviceCollection.AddScoped<LocalToGlobalConverterUtils>();
    serviceCollection.AddScoped<ArcGISColorManager>();
    serviceCollection.AddScoped<IHostObjectBuilder, ArcGISHostObjectBuilder>();

    serviceCollection.AddScoped<MapMembersUtils>();
  }
}
