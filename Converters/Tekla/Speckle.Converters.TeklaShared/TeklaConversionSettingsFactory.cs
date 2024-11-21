using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Tekla.Structures.Datatype;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024;

[GenerateAutoInterface]
public class TeklaConversionSettingsFactory(
  IHostToSpeckleUnitConverter<Distance.UnitType> unitsConverter,
  IConverterSettingsStore<TeklaConversionSettings> settingsStore
) : ITeklaConversionSettingsFactory
{
  public TeklaConversionSettings Current => settingsStore.Current;

  // NOTE: Distance.CurrentUnitType reflects Settings > Options > Units and decimals
  // Internal units (mm) are, however, always returned.
  // If model units != internal units, user can rely on units appended to each report parameter
  public TeklaConversionSettings Create(Model document, bool sendRebarsAsSolid) =>
    new(document, sendRebarsAsSolid, unitsConverter.ConvertOrThrow(Distance.CurrentUnitType));
}
