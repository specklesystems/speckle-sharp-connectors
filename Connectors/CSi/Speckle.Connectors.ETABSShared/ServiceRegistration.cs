using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABS22.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp;
using Speckle.Connectors.ETABSShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Services;
using Speckle.Converters.ETABSShared;

namespace Speckle.Connectors.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddEtabs(this IServiceCollection services)
  {
    services.AddEtabsConverters();
    services.AddScoped<CsiSendCollectionManager, EtabsSendCollectionManager>();

    // material services
    services.AddScoped<IMaterialUnpacker, EtabsMaterialUnpacker>();
    services.AddScoped<IApplicationMaterialPropertyExtractor, EtabsMaterialPropertyExtractor>();

    // section services
    services.AddScoped<ISectionUnpacker, EtabsSectionUnpacker>();
    services.AddScoped<IApplicationFrameSectionPropertyExtractor, EtabsFrameSectionPropertyExtractor>();
    services.AddScoped<IApplicationShellSectionPropertyExtractor, EtabsShellSectionPropertyExtractor>();
    services.AddScoped<EtabsSectionPropertyDefinitionService>();
    services.AddScoped<EtabsSectionPropertyExtractor>();
    services.AddScoped<EtabsShellSectionResolver>();

    return services;
  }
}
