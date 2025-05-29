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
  public static IServiceCollection AddCsi(
    this IServiceCollection services,
    Speckle.Sdk.Application application,
    HostAppVersion version
  )
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddDUISendOnly<CsiDocumentModelStore, DefaultThreadContext>(application, version);
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
    services.AddScoped<MaterialUnpacker>();
    services.AddScoped<IFrameSectionPropertyExtractor, CsiFrameSectionPropertyExtractor>();
    services.AddScoped<IShellSectionPropertyExtractor, CsiShellSectionPropertyExtractor>();

    // add converter caches
    services.AddScoped<CsiToSpeckleCacheSingleton>();

    return services;
  }
}
