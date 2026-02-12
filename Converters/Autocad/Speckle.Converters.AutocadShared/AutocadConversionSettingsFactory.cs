using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Autocad;

[GenerateAutoInterface]
public class AutocadConversionSettingsFactory(IHostToSpeckleUnitConverter<ADB.UnitsValue> unitsConverter)
  : IAutocadConversionSettingsFactory
{
  public AutocadConversionSettings Create(Document document)
  {
    AG.Matrix3d? m =
      document.Editor.CurrentUserCoordinateSystem == AG.Matrix3d.Identity
        ? null
        : document.Editor.CurrentUserCoordinateSystem;
    return new(document, m, unitsConverter.ConvertOrThrow(document.Database.Insunits));
  }
}
