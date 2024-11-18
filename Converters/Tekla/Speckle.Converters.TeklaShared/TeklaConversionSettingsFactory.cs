using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Tekla.Structures.Datatype;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024;

[GenerateAutoInterface]
public class TeklaConversionSettingsFactory(
  IHostToSpeckleUnitConverter<string> unitsConverter,
  IConverterSettingsStore<TeklaConversionSettings> settingsStore
) : ITeklaConversionSettingsFactory
{
  public TeklaConversionSettings Current => settingsStore.Current;

  // NOTE: Distance.CurrentUnitType reflects Settings > Options > Units and decimals, but internal units always returns mm
  // Therefore, hard-coding Distance.MILLIMETERS (for now)
  public TeklaConversionSettings Create(Model document, bool sendRebarsAsSolid) =>
    new(document, sendRebarsAsSolid, unitsConverter.ConvertOrThrow(Distance.MILLIMETERS));
}
