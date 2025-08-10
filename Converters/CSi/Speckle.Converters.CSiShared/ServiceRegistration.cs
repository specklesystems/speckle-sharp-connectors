using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Sdk;

namespace Speckle.Converters.CSiShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddCsiConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    // Register top-level converters
    serviceCollection.AddRootCommon<CsiRootToSpeckleConverter>(converterAssembly);

    // Register property extractors
    serviceCollection.AddScoped<CsiFramePropertiesExtractor>();
    serviceCollection.AddScoped<CsiJointPropertiesExtractor>();
    serviceCollection.AddScoped<CsiShellPropertiesExtractor>();
    serviceCollection.AddScoped<DatabaseTableExtractor>();
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<SharedPropertiesExtractor>();

    // Register results extractors
    serviceCollection.AddScoped<CsiBaseReactResultsExtractor>();
    serviceCollection.AddScoped<CsiFrameForceResultsExtractor>();
    serviceCollection.AddScoped<CsiPierForceResultsExtractor>();
    serviceCollection.AddScoped<CsiSpandrelForceResultsExtractor>();
    serviceCollection.AddScoped<ResultsArrayProcessor>();

    // Register connector caches
    serviceCollection.AddScoped<CsiToSpeckleCacheSingleton>();

    // Settings and unit conversions
    serviceCollection.AddApplicationConverters<CsiToSpeckleUnitConverter, eUnits>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<CsiConversionSettings>,
      ConverterSettingsStore<CsiConversionSettings>
    >();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
