using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converter.Navisworks;

public static class ServiceRegistration
{
  public static IServiceCollection AddNavisworksConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);
    // Register single root
    serviceCollection.AddRootCommon<NavisworksRootToSpeckleConverter>(converterAssembly);

    // register all application converters and context stacks
    serviceCollection.AddApplicationConverters<NavisworksToSpeckleUnitConverter, NAV.Units>(converterAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<NavisworksConversionSettings>,
      ConverterSettingsStore<NavisworksConversionSettings>
    >();
    return serviceCollection;
  }
}
