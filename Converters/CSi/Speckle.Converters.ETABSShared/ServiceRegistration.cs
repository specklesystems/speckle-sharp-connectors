using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.TopLevel;
using Speckle.Converters.ETABSShared.ToSpeckle.Helpers;
using Speckle.Converters.ETABSShared.ToSpeckle.TopLevel;
using Speckle.Sdk;

namespace Speckle.Converters.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddEtabsConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    // Etabs-specific implementations
    serviceCollection.AddScoped<EtabsFramePropertiesExtractor>();
    serviceCollection.AddScoped<EtabsJointPropertiesExtractor>();
    serviceCollection.AddScoped<EtabsShellPropertiesExtractor>();
    serviceCollection.AddScoped<IApplicationPropertiesExtractor, EtabsPropertiesExtractor>();
    serviceCollection.AddScoped<CsiObjectToSpeckleConverterBase, EtabsObjectToSpeckleConverter>();
    serviceCollection.AddScoped<EtabsShellSectionResolver>();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
