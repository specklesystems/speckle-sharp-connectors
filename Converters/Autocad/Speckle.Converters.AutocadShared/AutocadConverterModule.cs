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
    // add single root converter
    builder.AddConverters<AutocadRootToHostConverter>();

    // add application converters and context stack
    builder.AddApplicationConverters<AutocadToSpeckleUnitConverter, UnitsValue>();
    builder.AddScoped<IConversionContextStack<Document, UnitsValue>, AutocadConversionContextStack>();
  }
}