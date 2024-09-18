using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Autocad;

[GenerateAutoInterface]
public class AutocadConversionSettingsFactory(IHostToSpeckleUnitConverter<ADB.UnitsValue> unitsConverter)
  : IAutocadConversionSettingsFactory
{
  public AutocadConversionSettings Create(Document document) =>
    new(document, unitsConverter.ConvertOrThrow(document.Database.Insunits));
}
