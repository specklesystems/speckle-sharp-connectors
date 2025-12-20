using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converters.Plant3dShared;

public static class ServiceRegistration
{
  public static void AddPlant3dConverters(this IServiceCollection serviceCollection)
  {
    var plant3dAssembly = typeof(Plant3dConversionSettings).Assembly;
    var autocadAssembly = typeof(AutocadConversionSettings).Assembly;
    
    serviceCollection.AddMatchingInterfacesAsTransient(plant3dAssembly);
    serviceCollection.AddMatchingInterfacesAsTransient(autocadAssembly);
    
    serviceCollection.AddRootCommon<Plant3dRootToSpeckleConverter>(plant3dAssembly);

    serviceCollection.AddApplicationConverters<Plant3dToSpeckleUnitConverter, ADB.UnitsValue>(
      plant3dAssembly
    );
    serviceCollection.AddApplicationConverters<AutocadToSpeckleUnitConverter, ADB.UnitsValue>(autocadAssembly);
    
    serviceCollection.AddScoped<
      IConverterSettingsStore<Plant3dConversionSettings>,
      ConverterSettingsStore<Plant3dConversionSettings>
    >();
    serviceCollection.AddScoped<
      IConverterSettingsStore<AutocadConversionSettings>,
      ConverterSettingsStore<AutocadConversionSettings>
    >();
  }
}

