using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converter.MicroStation.Services;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converter.MicroStation.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Converter.MicroStation.DependencyInjection;

public static class MicroStationConverterServiceRegistration
{
  /// <summary>
  /// Registers all converter services. The caller (connector DI) must also register
  /// <see cref="Application"/> as a singleton before calling this.
  /// </summary>
  public static IServiceCollection AddMicroStationConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    // Auto-discover and register all IToSpeckleTopLevelConverter implementations
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    // Register the root dispatcher
    serviceCollection.AddRootCommon<MicroStationRootToSpeckleConverter>(converterAssembly);

    // Unit converter
    serviceCollection.AddSingleton<MicroStationToSpeckleUnitConverter>();
    serviceCollection.AddSingleton<IHostToSpeckleUnitConverter<MeasurementUnit>>(
      sp => sp.GetRequiredService<MicroStationToSpeckleUnitConverter>()
    );

    // Conversion settings factory and store
    serviceCollection.AddSingleton<IMicroStationConversionSettingsFactory, MicroStationConversionSettingsFactory>();
    serviceCollection.AddScoped<IConverterSettingsStore<MicroStationConversionSettings>, ConverterSettingsStore<MicroStationConversionSettings>>();

    return serviceCollection;
  }
}
