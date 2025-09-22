using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Autocad;

[GenerateAutoInterface]
public class AutocadConversionSettingsFactory(IHostToSpeckleUnitConverter<ADB.UnitsValue> unitsConverter)
  : IAutocadConversionSettingsFactory
{
  public AutocadConversionSettings Create(Document document) =>
    new(
      document,
      document.Editor.CurrentUserCoordinateSystem,
      unitsConverter.ConvertOrThrow(document.Database.Insunits)
    );
}
