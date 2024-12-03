using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.TopLevel;
using Speckle.Sdk;

namespace Speckle.Converters.CSiShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddCSiConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();

    serviceCollection.AddTransient<CSiObjectToSpeckleConverter>();
    serviceCollection.AddScoped<DisplayValueExtractor>();

    // TODO: Property extractor

    serviceCollection.AddRootCommon<CSiRootToSpeckleConverter>(converterAssembly);
    serviceCollection.AddApplicationConverters<CSiToSpeckleUnitConverter, eUnits>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<CSiConversionSettings>,
      ConverterSettingsStore<CSiConversionSettings>
    >();

    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    return serviceCollection;
  }
}
