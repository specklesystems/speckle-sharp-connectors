using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Autocad;
using Speckle.Converters.Autocad.ToHost.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
// using Speckle.Converters.Plant3dShared.Helpers;
using Speckle.Converters.Plant3dShared.ToSpeckle;
using Speckle.Sdk;

namespace Speckle.Converters.Plant3dShared;

public static class ServiceRegistration
{
  public static void AddPlant3dConverters(this IServiceCollection serviceCollection)
  {
    var plant3dAssembly = typeof(Plant3dConversionSettings).Assembly;
    var autocadAssembly = typeof(AutocadConversionSettings).Assembly;
    // register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(plant3dAssembly);
    serviceCollection.AddMatchingInterfacesAsTransient(autocadAssembly);
    // Register single root
    serviceCollection.AddRootCommon<Plant3dRootToSpeckleConverter>(plant3dAssembly);

    // register all application converters
    serviceCollection.AddApplicationConverters<Plant3dToSpeckleUnitConverter, AAEC.BuiltInUnit>(plant3dAssembly);
    serviceCollection.AddApplicationConverters<AutocadToSpeckleUnitConverter, ADB.UnitsValue>(autocadAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<Plant3dConversionSettings>,
      ConverterSettingsStore<Plant3dConversionSettings>
    >();
    serviceCollection.AddScoped<
      IConverterSettingsStore<AutocadConversionSettings>,
      ConverterSettingsStore<AutocadConversionSettings>
    >();
    serviceCollection.AddScoped<IReferencePointConverter, ReferencePointConverter>();

    // add other classes
    serviceCollection.AddScoped<Speckle.Converters.Plant3dShared.ToSpeckle.PropertiesExtractor>(); // for plant3d
    serviceCollection.AddScoped<Speckle.Converters.AutocadShared.ToSpeckle.IPropertiesExtractor, PropertiesExtractor>();
    serviceCollection.AddScoped<PropertySetExtractor>();
    serviceCollection.AddScoped<PropertySetDefinitionHandler>();
    serviceCollection.AddScoped<ExtensionDictionaryExtractor>();
    serviceCollection.AddScoped<Plant3dDataExtractor>();
    serviceCollection.AddScoped<EntityUnitConverter>();
  }
}
