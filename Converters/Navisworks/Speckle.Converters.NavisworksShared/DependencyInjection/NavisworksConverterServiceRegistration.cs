using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle;
using Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converter.Navisworks.DependencyInjection;

public static class NavisworksConverterServiceRegistration
{
  public static IServiceCollection AddNavisworksConverter(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    // Register base converters
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);
    serviceCollection.AddRootCommon<NavisworksRootToSpeckleConverter>(converterAssembly);

    // Register property handlers
    serviceCollection.AddScoped<StandardPropertyHandler>();
    serviceCollection.AddScoped<HierarchicalPropertyHandler>();

    // Register settings management
    serviceCollection.AddScoped<INavisworksConversionSettingsFactory, NavisworksConversionSettingsFactory>();
    serviceCollection.AddScoped<
      IConverterSettingsStore<NavisworksConversionSettings>,
      ConverterSettingsStore<NavisworksConversionSettings>
    >();

    // Register unit conversion
    serviceCollection.AddSingleton<IHostToSpeckleUnitConverter<NAV.Units>, NavisworksToSpeckleUnitConverter>();

    // Register converters and handlers
    serviceCollection.AddApplicationConverters<NavisworksToSpeckleUnitConverter, NAV.Units>(converterAssembly);
    serviceCollection.AddScoped<ModelPropertiesExtractor>();
    serviceCollection.AddScoped<PrimitiveProcessor>();
    serviceCollection.AddScoped<PropertySetsExtractor>();

    // Register geometry conversion
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<GeometryToSpeckleConverter>();

    return serviceCollection;
  }
}
