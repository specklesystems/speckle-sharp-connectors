using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Tekla.Structures.Model;
using TSDT = Tekla.Structures.Datatype;

namespace Speckle.Converter.Tekla2024;

[GenerateAutoInterface]
public class TeklaConversionSettingsFactory(
  IHostToSpeckleUnitConverter<TSDT.Distance.UnitType> unitsConverter,
  IConverterSettingsStore<TeklaConversionSettings> settingsStore
) : ITeklaConversionSettingsFactory
{
  public TeklaConversionSettings Current => settingsStore.Current;

  public TeklaConversionSettings Create(Model document, bool sendRebarsAsSolid) =>
    new(document, sendRebarsAsSolid, unitsConverter.ConvertOrThrow(TSDT.Distance.CurrentUnitType));
}
