using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.Sdk;

namespace Speckle.Converters.RevitShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddRevitConverters(this IServiceCollection serviceCollection)
  {
    var converterAssembly = Assembly.GetExecutingAssembly();
    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(converterAssembly);

    // register single root
    serviceCollection.AddRootCommon<RevitRootToSpeckleConverter>(converterAssembly);

    // register all application converters
    serviceCollection.AddApplicationConverters<RevitToSpeckleUnitConverter, DB.ForgeTypeId>(converterAssembly);

    serviceCollection.AddScoped<IRootToHostConverter, RevitRootToHostConverter>();
    serviceCollection.AddSingleton(new RevitContext());

    serviceCollection.AddSingleton(new RevitToHostCacheSingleton());
    serviceCollection.AddSingleton(new RevitToSpeckleCacheSingleton());

    // POC: do we need ToSpeckleScalingService as is, do we need to interface it out?
    serviceCollection.AddScoped<ScalingServiceToSpeckle>();
    serviceCollection.AddScoped<ScalingServiceToHost>();

    // POC: the concrete type can come out if we remove all the reference to it
    serviceCollection.AddScoped<
      IConverterSettingsStore<RevitConversionSettings>,
      ConverterSettingsStore<RevitConversionSettings>
    >();

    serviceCollection.AddScoped<IReferencePointConverter, ReferencePointConverter>();

    serviceCollection.AddScoped<IRevitVersionConversionHelper, RevitVersionConversionHelper>();

    // register extractors
    serviceCollection.AddScoped<ParameterValueExtractor>();
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<LevelExtractor>();
    serviceCollection.AddScoped<ParameterDefinitionHandler>();
    serviceCollection.AddScoped<ParameterExtractor>();
    serviceCollection.AddScoped<ClassPropertiesExtractor>();
    serviceCollection.AddScoped<PropertiesExtractor>();
    serviceCollection.AddScoped<StructuralMaterialAssetExtractor>();

    return serviceCollection;
  }
}
