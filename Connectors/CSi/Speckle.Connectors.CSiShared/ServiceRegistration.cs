using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.CSiShared.Bindings;
using Speckle.Connectors.CSiShared.Builders;
using Speckle.Connectors.CSiShared.Filters;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.CSiShared.Operations.Send.Settings;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Connectors.CSiShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddCsi(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddConnectors();
    services.AddDUI<DefaultThreadContext, CsiDocumentModelStore>();
    services.AddDUIView();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, CsiSharedBasicConnectorBinding>();

    services.AddSingleton<IBinding, CsiSharedSelectionBinding>();
    services.AddSingleton<IBinding, CsiSharedSendBinding>();

    services.AddScoped<ISendFilter, CsiSharedSelectionFilter>();
    services.AddScoped<CsiSendCollectionManager>();
    services.AddScoped<IRootObjectBuilder<ICsiWrapper>, CsiRootObjectBuilder>();
    services.AddScoped<SendOperation<ICsiWrapper>>();

    services.AddScoped<CsiMaterialPropertyExtractor>();
    services.AddScoped<CsiResultsExtractorFactory>();
    services.AddScoped<IMaterialPropertyExtractor, CsiMaterialPropertyExtractor>();
    services.AddScoped<IFrameSectionPropertyExtractor, CsiFrameSectionPropertyExtractor>();
    services.AddScoped<IShellSectionPropertyExtractor, CsiShellSectionPropertyExtractor>();
    services.AddScoped<AnalysisResultsExtractor>();

    // add converter caches
    services.AddScoped<CsiToSpeckleCacheSingleton>();

    // add settings manager
    services.AddScoped<ToSpeckleSettingsManager>();

    return services;
  }
}
