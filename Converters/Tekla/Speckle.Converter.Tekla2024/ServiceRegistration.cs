using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Tekla.Structures.Drawing;

namespace Speckle.Converter.Tekla2024;

public static class ServiceRegistration
{
  public static IServiceCollection AddTeklaConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);
    serviceCollection.AddRootCommon<RootToSpeckleConverter>(converterAssembly);

    serviceCollection.AddApplicationConverters<TeklaToSpeckleUnitConverter, Units>(converterAssembly);
    serviceCollection.AddApplicationConverters<TeklaToSpeckleUnitConverter, Units>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<TeklaConversionSettings>,
      ConverterSettingsStore<TeklaConversionSettings>
    >();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
