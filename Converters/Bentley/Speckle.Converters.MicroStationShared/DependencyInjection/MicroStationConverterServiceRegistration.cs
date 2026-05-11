using Microsoft.Extensions.DependencyInjection;
using Speckle.Converter.MicroStation.Services;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converter.MicroStation.ToSpeckle;
using Speckle.Converter.MicroStation.ToSpeckle.TopLevel;
using Speckle.Converters.Common;

namespace Speckle.Converter.MicroStation.DependencyInjection;

/// <summary>
/// Wires up the managed-API conversion pipeline. Each top-level converter is a plain class with
/// a typed <c>Convert(MgdXxxElement)</c> method — no <c>IToSpeckleTopLevelConverter</c> auto-
/// discovery, no <c>NameAndRankValue</c> attribute, no <c>IConverterManager</c> resolution. The
/// root dispatcher pattern-matches managed element types and dispatches directly to the right
/// converter via constructor injection, which keeps the dispatch table compile-time-checked.
/// </summary>
public static class MicroStationConverterServiceRegistration
{
  public static IServiceCollection AddMicroStationConverters(this IServiceCollection serviceCollection)
  {
    // Root dispatcher — single entry point that the SendBinding / RootObjectBuilder calls.
    // No leaf converter takes a back-reference to this; container elements (CellHeader) get
    // their children walked by the dispatcher itself via private recursion, so there's no
    // DI cycle and no need for Lazy<> / IServiceProvider service-locator workarounds.
    serviceCollection.AddSingleton<IRootToSpeckleConverter, MicroStationRootToSpeckleConverter>();

    // Per-type managed converters (transient — they're stateless).
    serviceCollection.AddTransient<LineElementConverter>();
    serviceCollection.AddTransient<ArcElementConverter>();
    serviceCollection.AddTransient<EllipseElementConverter>();
    serviceCollection.AddTransient<LineStringElementConverter>();
    serviceCollection.AddTransient<PointStringElementConverter>();
    serviceCollection.AddTransient<ShapeElementConverter>();
    serviceCollection.AddTransient<ComplexShapeElementConverter>();
    serviceCollection.AddTransient<ComplexStringElementConverter>();
    serviceCollection.AddTransient<BsplineCurveElementConverter>();
    serviceCollection.AddTransient<BSplineSurfaceElementConverter>();
    serviceCollection.AddTransient<CellHeaderElementConverter>();
    serviceCollection.AddTransient<SharedCellElementConverter>();
    serviceCollection.AddTransient<TextElementConverter>();
    serviceCollection.AddTransient<SolidElementConverter>();
    serviceCollection.AddTransient<MeshHeaderElementConverter>();

    // Bounding-box fallback for any managed element that doesn't match the dispatch table or
    // whose dedicated converter throws.
    serviceCollection.AddTransient<FallbackElementMeshConverter>();

    // Unit converter
    serviceCollection.AddSingleton<MicroStationToSpeckleUnitConverter>();
    serviceCollection.AddSingleton<IHostToSpeckleUnitConverter<MeasurementUnit>>(sp =>
      sp.GetRequiredService<MicroStationToSpeckleUnitConverter>()
    );

    // Conversion settings factory + per-scope store
    serviceCollection.AddSingleton<IMicroStationConversionSettingsFactory, MicroStationConversionSettingsFactory>();
    serviceCollection.AddScoped<
      IConverterSettingsStore<MicroStationConversionSettings>,
      ConverterSettingsStore<MicroStationConversionSettings>
    >();

    return serviceCollection;
  }
}
