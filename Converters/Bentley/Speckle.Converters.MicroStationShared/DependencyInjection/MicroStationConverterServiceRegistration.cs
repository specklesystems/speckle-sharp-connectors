using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Converters.MicroStation.ToSpeckle;
using Speckle.Converters.MicroStation.ToSpeckle.Appearance;
using Speckle.Converters.MicroStation.ToSpeckle.MeshExtraction;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Converters.MicroStation.ToSpeckle.Raw;

namespace Speckle.Converters.MicroStation.DependencyInjection;

/// <summary>
/// Wires the managed conversion pipeline. There is no <c>IToSpeckleTopLevelConverter</c> /
/// <c>IConverterManager</c> discovery here — the DGN element hierarchy is closed and small, so the
/// <see cref="DisplayValueExtractor"/> dispatches by pattern-matching managed element types, which
/// keeps the dgnextract strategy order compile-time-checked. The connector's root object builder
/// consumes <see cref="DisplayValueExtractor"/> + <see cref="PropertiesExtractor"/> directly.
/// </summary>
public static class MicroStationConverterServiceRegistration
{
  public static IServiceCollection AddMicroStationConverters(this IServiceCollection serviceCollection)
  {
    // Per-operation stateful services (transform stack, ByCell colour stack) — scoped, same
    // lifetime as the converter settings store the send pipeline creates per operation.
    serviceCollection.AddScoped<GeometryMapper>();
    serviceCollection.AddScoped<AppearanceResolver>();
    serviceCollection.AddScoped<PolyfaceConverter>();
    serviceCollection.AddScoped<CurvePrimitiveConverter>();
    serviceCollection.AddScoped<GraphicsCaptureExtractor>();
    serviceCollection.AddScoped<TextConverter>();
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<SharedCellInstanceSink>();
    serviceCollection.AddScoped<PropertiesExtractor>();

    // Unit converter
    serviceCollection.AddSingleton<
      IHostToSpeckleUnitConverter<DPN.UnitDefinition>,
      MicroStationToSpeckleUnitConverter
    >();

    // Conversion settings factory + per-scope store
    serviceCollection.AddSingleton<IMicroStationConversionSettingsFactory, MicroStationConversionSettingsFactory>();
    serviceCollection.AddScoped<
      IConverterSettingsStore<MicroStationConversionSettings>,
      ConverterSettingsStore<MicroStationConversionSettings>
    >();

    return serviceCollection;
  }
}
