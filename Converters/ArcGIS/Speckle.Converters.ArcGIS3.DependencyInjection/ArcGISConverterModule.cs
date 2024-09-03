using ArcGIS.Core.Geometry;
using Speckle.Autofac.DependencyInjection;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.DependencyInjection;

namespace Speckle.Converters.ArcGIS3.DependencyInjection;

public class ArcGISConverterModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    // add single root converter
    //don't need a host specific RootToSpeckleConverter
    builder.AddRootCommon<RootToSpeckleConverter>();

    // add application converters
    builder.AddApplicationConverters<ArcGISToSpeckleUnitConverter, Unit>();

    // most things should be InstancePerLifetimeScope so we get one per operation
    builder.AddScoped<IFeatureClassUtils, FeatureClassUtils>();
    builder.AddScoped<ICrsUtils, CrsUtils>();
    builder.AddScoped<IArcGISFieldUtils, ArcGISFieldUtils>();
    builder.AddScoped<ILocalToGlobalConverterUtils, LocalToGlobalConverterUtils>();
    builder.AddScoped<ICharacterCleaner, CharacterCleaner>();

    // single stack per conversion
    builder.AddScoped<
      IConverterSettingsStore<ArcGISConversionSettings>,
      ConverterSettingsStore<ArcGISConversionSettings>
    >();
  }
}
