using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.TeklaShared.Bindings;
using Speckle.Connectors.TeklaShared.Filters;
using Speckle.Connectors.TeklaShared.HostApp;
using Speckle.Connectors.TeklaShared.Operations.Send;
using Speckle.Connectors.TeklaShared.Operations.Send.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.TeklaShared;
using Speckle.Sdk;
using Speckle.Sdk.Models.GraphTraversal;
using Tekla.Structures.Model;

namespace Speckle.Connectors.TeklaShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddTekla(this IServiceCollection services, HostAppVersion version)
  {
    var converterAssembly = System.Reflection.Assembly.GetExecutingAssembly();

    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddDUISendOnly<TeklaDocumentModelStore, DefaultThreadContext>(HostApplications.TeklaStructures, version);
    services.AddDUIView();

    services.AddSingleton<IAppIdleManager, TeklaIdleManager>();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();
    services.AddSingleton<IBasicConnectorBinding, TeklaBasicConnectorBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBinding, TeklaSendBinding>();
    services.AddSingleton<IBinding, TeklaSelectionBinding>();

    services.AddSingleton<Model>();
    services.AddSingleton<Events>();
    services.AddSingleton<Tekla.Structures.Model.UI.ModelObjectSelector>();

    services.AddScoped<ISendFilter, TeklaSelectionFilter>();
    services.AddSingleton<ISendConversionCache, SendConversionCache>();
    services.AddScoped<SendCollectionManager>();
    services.AddScoped<IRootObjectBuilder<ModelObject>, TeklaRootObjectBuilder>();
    services.AddScoped<SendOperation<ModelObject>>();

    services.AddSingleton<ToSpeckleSettingsManager>();

    services.AddScoped<TraversalContext>();
    services.AddScoped<
      IConverterSettingsStore<TeklaConversionSettings>,
      ConverterSettingsStore<TeklaConversionSettings>
    >();

    // Register unpackers and bakers
    services.AddScoped<TeklaMaterialUnpacker>();

    services.AddMatchingInterfacesAsTransient(converterAssembly);

    return services;
  }
}
