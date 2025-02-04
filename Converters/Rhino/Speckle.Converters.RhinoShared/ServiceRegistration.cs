using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Rhino.HostApp.Properties;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converters.Rhino;

public static class ServiceRegistration
{
  public static IServiceCollection AddRhinoConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);
    // Register single root
    serviceCollection.AddRootCommon<RootToSpeckleConverter>(converterAssembly);

    // register all application converters and context stacks
    serviceCollection.AddApplicationConverters<RhinoToSpeckleUnitConverter, UnitSystem>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<RhinoConversionSettings>,
      ConverterSettingsStore<RhinoConversionSettings>
    >();

    // register helper classes
    serviceCollection.AddScoped<PropertiesExtractor>();
    serviceCollection.AddScoped<UserStringsExtractor>();

    return serviceCollection;
  }
}
