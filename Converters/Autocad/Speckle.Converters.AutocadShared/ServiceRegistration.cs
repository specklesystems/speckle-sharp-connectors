using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.AutocadShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converters.Autocad;

public static class ServiceRegistration
{
  public static void AddAutocadConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);
    // add single root converter
    serviceCollection.AddRootCommon<AutocadRootToSpeckleConverter>(converterAssembly);

    // add application converters and context stack
    serviceCollection.AddApplicationConverters<AutocadToSpeckleUnitConverter, ADB.UnitsValue>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<AutocadConversionSettings>,
      ConverterSettingsStore<AutocadConversionSettings>
    >();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());

    // add other classes
    serviceCollection.AddScoped<PropertiesExtractor>();
    serviceCollection.AddScoped<IPropertiesExtractor, PropertiesExtractor>();
    serviceCollection.AddScoped<ExtensionDictionaryExtractor>();
    serviceCollection.AddScoped<XDataExtractor>();
  }
}
