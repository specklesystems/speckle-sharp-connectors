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
using Speckle.Connectors.TSDShared.Bindings;
using Speckle.Connectors.TSDShared.Filters;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Connectors.TSDShared.Operations.Send;
using Speckle.Connectors.TSDShared.Operations.Send.Results;
using TSD.API.Remoting.Common;

namespace Speckle.Connectors.TSDShared;

internal static class ServiceRegistration
{
  public static IServiceCollection AddTSD(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();

    services.AddConnectors();
    services.AddDUI<DefaultThreadContext, TSDDocumentModelStore>();
    services.AddDUIView();

    services.AddSingleton<TSDApplicationService>();
    services.AddSingleton<ITSDApplicationService>(sp => sp.GetRequiredService<TSDApplicationService>());

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, TSDBasicConnectorBinding>();

    services.AddSingleton<IBinding, TSDSelectionBinding>();
    services.AddSingleton<IBinding, TSDSendBinding>();
    services.AddScoped<ISendFilter, TSDSelectionFilter>();

    services.AddSingleton<ISendConversionCache, NullSendConversionCache>();
    services.AddSingleton<IOperationProgressManager, OperationProgressManager>();
    services.AddScoped<TsdDisplayValueExtractor>();
    services.AddScoped<TsdMemberPropertyExtractor>();
    services.AddScoped<TsdSlabPropertyExtractor>();
    services.AddScoped<TsdWallPropertyExtractor>();
    services.AddScoped<TsdConversionSettings>();
    services.AddScoped<TsdElementEndForceResultsExtractor>();
    services.AddScoped<TsdOffsetForceResultsExtractor>();
    services.AddScoped<TsdReactionResultsExtractor>();
    services.AddScoped<TsdSlabForceResultsExtractor>();
    services.AddScoped<TsdWallForceResultsExtractor>();
    services.AddScoped<TsdNodalDisplacementResultsExtractor>();
    services.AddScoped<TsdOffsetDisplacementResultsExtractor>();
    services.AddScoped<TsdResultLineForceResultsExtractor>();
    services.AddScoped<TsdWallLineForceResultsExtractor>();
    services.AddScoped<TsdResultsExtractorFactory>();
    services.AddScoped<TsdAnalysisResultsExtractor>();
    services.AddScoped<IRootObjectBuilder<IEntity>, TsdRootObjectBuilder>();
    services.AddScoped<SendOperation<IEntity>>();

    return services;
  }
}
