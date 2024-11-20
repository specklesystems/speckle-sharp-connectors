using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converter.Tekla2024.ToSpeckle.Helpers;
using Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Tekla.Structures.Datatype;

namespace Speckle.Converter.Tekla2024;

public static class ServiceRegistration
{
  public static IServiceCollection AddTeklaConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    serviceCollection.AddTransient<ModelObjectToSpeckleConverter>();

    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<ClassPropertyExtractor>();
    serviceCollection.AddScoped<ReportPropertyExtractor>();

    serviceCollection.AddRootCommon<TeklaRootToSpeckleConverter>(converterAssembly);
    serviceCollection.AddApplicationConverters<TeklaToSpeckleUnitConverter, Distance.UnitType>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<TeklaConversionSettings>,
      ConverterSettingsStore<TeklaConversionSettings>
    >();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
