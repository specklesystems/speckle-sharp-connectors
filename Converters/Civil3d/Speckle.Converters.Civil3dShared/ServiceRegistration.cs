using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Civil3dShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;

namespace Speckle.Converters.Civil3dShared;

public static class ServiceRegistration
{
  public static void AddCivil3dConverters(this IServiceCollection serviceCollection)
  {
    var civil3dAssembly = typeof(Civil3dConversionSettings).Assembly;
    var autocadAssembly = typeof(AutocadConversionSettings).Assembly;
    //register types by default
    serviceCollection.AddMatchingInterfacesAsTransient(civil3dAssembly);
    serviceCollection.AddMatchingInterfacesAsTransient(autocadAssembly);
    // Register single root
    serviceCollection.AddRootCommon<Civil3dRootToSpeckleConverter>(civil3dAssembly);

    // register all application converters
    serviceCollection.AddApplicationConverters<Civil3dToSpeckleUnitConverter, Autodesk.Aec.BuiltInUnit>(
      civil3dAssembly
    );
    serviceCollection.AddApplicationConverters<AutocadToSpeckleUnitConverter, ADB.UnitsValue>(autocadAssembly);
    serviceCollection.AddScoped<
      IConverterSettingsStore<Civil3dConversionSettings>,
      ConverterSettingsStore<Civil3dConversionSettings>
    >();
    serviceCollection.AddScoped<
      IConverterSettingsStore<AutocadConversionSettings>,
      ConverterSettingsStore<AutocadConversionSettings>
    >();

    // add other classes
    serviceCollection.AddScoped<PartDataExtractor>();
    serviceCollection.AddScoped<DisplayValueExtractor>();
    serviceCollection.AddScoped<BaseCurveExtractor>();
    serviceCollection.AddScoped<PropertySetExtractor>();
    serviceCollection.AddScoped<PropertySetDefinitionHandler>();
    serviceCollection.AddScoped<GeneralPropertiesExtractor>();
    serviceCollection.AddScoped<ClassPropertiesExtractor>();
    serviceCollection.AddScoped<CatchmentGroupHandler>();
    serviceCollection.AddScoped<PipeNetworkHandler>();
    serviceCollection.AddScoped<CorridorHandler>();
  }
}
