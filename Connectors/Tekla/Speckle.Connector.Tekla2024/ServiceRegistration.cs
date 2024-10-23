using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Tekla2024.Bindings;
using Speckle.Connector.Tekla2024.Filters;
using Speckle.Connector.Tekla2024.HostApp;
using Speckle.Connector.Tekla2024.Operations.Send;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converter.Tekla2024;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.GraphTraversal;
using Tekla.Structures.Model;

namespace Speckle.Connector.Tekla2024;

public static class ServiceRegistration
{
  public static IServiceCollection AddTekla(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddConnectorUtils();
    services.AddDUI();
    services.AddDUIView();

    services.AddSingleton<DocumentModelStore, TeklaDocumentModelStore>();
    services.AddSingleton<IAppIdleManager, TeklaIdleManager>();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();
    services.AddSingleton<IBasicConnectorBinding, TeklaBasicConnectorBinding>();
    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBinding, TeklaSendBinding>();
    services.AddSingleton<IBinding, TeklaSelectionBinding>();

    services.RegisterTopLevelExceptionHandler();

    services.AddSingleton<Model>();
    services.AddSingleton<Events>();
    services.AddSingleton<Tekla.Structures.Model.UI.ModelObjectSelector>();

    services.AddScoped<ISendFilter, TeklaSelectionFilter>();
    services.AddSingleton<ISendConversionCache, SendConversionCache>();
    services.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    services.AddScoped<IRootObjectBuilder<ModelObject>, TeklaRootObjectBuilder>();
    services.AddScoped<SendOperation<ModelObject>>();

    services.AddTransient<CancellationManager>();
    services.AddSingleton<IOperationProgressManager, OperationProgressManager>();

    services.AddScoped<TraversalContext>();
    services.AddScoped<
      IConverterSettingsStore<TeklaConversionSettings>,
      ConverterSettingsStore<TeklaConversionSettings>
    >();

    return services;
  }
}
