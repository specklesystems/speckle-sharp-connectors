using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converter.Tekla2024.ToSpeckle.Helpers;
using Speckle.Converter.Tekla2024.ToSpeckle.Raw;
using Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Tekla.Structures.Drawing;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024;

public static class ServiceRegistration
{
  public static IServiceCollection AddTeklaConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    
    serviceCollection.AddTransient<ModelObjectToSpeckleConverter>();
    
    serviceCollection.AddTransient<ITypedConverter<TG.Point, SOG.Point>, TeklaPointConverter>();
    
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<PropertyExtractor>();
    
    serviceCollection.AddRootCommon<TeklaRootToSpeckleConverter>(converterAssembly);
    serviceCollection.AddApplicationConverters<TeklaToSpeckleUnitConverter, Units>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<TeklaConversionSettings>,
      ConverterSettingsStore<TeklaConversionSettings>
    >();
    
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
