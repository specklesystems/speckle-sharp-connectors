using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.ArcGIS3.ToSpeckle.Helpers;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converters.ArcGIS3;

public static class ArcGISConverterModule
{
  public static void AddArcGISConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);
    // add single root converter
    //don't need a host specific RootToSpeckleConverter
    serviceCollection.AddRootCommon<RootToSpeckleConverter>(converterAssembly);

    // add application converters
    serviceCollection.AddApplicationConverters<ArcGISToSpeckleUnitConverter, ACG.Unit>(converterAssembly);

    // most things should be InstancePerLifetimeScope so we get one per operation
    serviceCollection.AddScoped<IFeatureClassUtils, FeatureClassUtils>();
    serviceCollection.AddScoped<IArcGISFieldUtils, ArcGISFieldUtils>();
    serviceCollection.AddScoped<LocalToGlobalConverterUtils>();
    serviceCollection.AddScoped<ICharacterCleaner, CharacterCleaner>();
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<PropertiesExtractor>();

    // single stack per conversion
    serviceCollection.AddScoped<
      IConverterSettingsStore<ArcGISConversionSettings>,
      ConverterSettingsStore<ArcGISConversionSettings>
    >();
  }
}
