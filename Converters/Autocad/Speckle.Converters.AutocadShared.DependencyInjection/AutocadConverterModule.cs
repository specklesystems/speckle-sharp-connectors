using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Autofac.DependencyInjection;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.DependencyInjection;

namespace Speckle.Converters.AutocadShared.DependencyInjection;

public class AutocadConverterModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    //register types by default
    builder.ScanAssemblyOfType<AutocadConversionSettingsFactory>();
    // add single root converter
    builder.AddRootCommon<AutocadRootToHostConverter>();

    // add application converters and context stack
    builder.AddApplicationConverters<AutocadToSpeckleUnitConverter, UnitsValue>();
    builder.AddScoped<
      IConverterSettingsStore<AutocadConversionSettings>,
      ConverterSettingsStore<AutocadConversionSettings>
    >();
  }
}
