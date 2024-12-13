using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.Rhino;
using Speckle.Sdk;

namespace Speckle.Converters.Grasshopper;

public static class ServiceRegistration
{
  public static IServiceCollection AddRhinoConverters(this IServiceCollection serviceCollection)
  {
    var rhinoAssembly = typeof(RhinoConversionSettings).Assembly;
    var grasshopperAssembly = typeof(GrasshopperConversionSettings).Assembly;

    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(rhinoAssembly);
    serviceCollection.AddMatchingInterfacesAsTransient(grasshopperAssembly);

    // Register single root
    serviceCollection.AddRootCommon<GrasshopperRootToSpeckleConverter>(grasshopperAssembly);

    // register all application converters and context stacks
    serviceCollection.AddApplicationConverters<RhinoToSpeckleUnitConverter, UnitSystem>(rhinoAssembly);
    serviceCollection.AddApplicationConverters<RhinoToSpeckleUnitConverter, UnitSystem>(grasshopperAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<RhinoConversionSettings>,
      ConverterSettingsStore<RhinoConversionSettings>
    >();
    serviceCollection.AddScoped<
      IConverterSettingsStore<GrasshopperConversionSettings>,
      ConverterSettingsStore<GrasshopperConversionSettings>
    >();

    return serviceCollection;
  }
}
