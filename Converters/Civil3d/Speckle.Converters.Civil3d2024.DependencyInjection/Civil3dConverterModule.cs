using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Autofac.DependencyInjection;
using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3d;
using Speckle.Converters.Common;
using Speckle.Converters.Common.DependencyInjection;

namespace Speckle.Converters.Civil3d2024.DependencyInjection;

public class Civil3dConverterModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    //register types by default
    builder.ScanAssemblyOfType<Civil3dConversionSettings>();
    builder.ScanAssemblyOfType<AutocadConversionSettings>();
    // Register single root
    builder.AddRootCommon<Civil3dRootToHostConverter>();

    // register all application converters
    builder.AddApplicationConverters<Civil3dToSpeckleUnitConverter, Autodesk.Aec.BuiltInUnit>();
    builder.AddApplicationConverters<AutocadToSpeckleUnitConverter, UnitsValue>();
    builder.AddScoped<
      IConverterSettingsStore<Civil3dConversionSettings>,
      ConverterSettingsStore<Civil3dConversionSettings>
    >();
    builder.AddScoped<
      IConverterSettingsStore<AutocadConversionSettings>,
      ConverterSettingsStore<AutocadConversionSettings>
    >();
  }
}
