using Rhino;
using Speckle.Autofac.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.DependencyInjection;
using Speckle.Converters.Rhino;
using Speckle.Converters.Rhino.ToSpeckle.Raw;

namespace Speckle.Converters.Rhino8.DependencyInjection;

public class RhinoConverterModule : ISpeckleModule
{
  public void Load(SpeckleContainerBuilder builder)
  {
    //register types by default
    builder.ScanAssemblyOfType<BoxFactory>();
    // Register single root
    builder.AddRootCommon<RootToSpeckleConverter>();

    // register all application converters and context stacks
    builder.AddApplicationConverters<RhinoToSpeckleUnitConverter, UnitSystem>();
    builder.AddScoped<
      IConverterSettingsStore<RhinoConversionSettings>,
      ConverterSettingsStore<RhinoConversionSettings>
    >();
  }
}
