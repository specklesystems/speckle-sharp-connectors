using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Tekla.Structures.Model;
using TSD = Tekla.Structures.Drawing;

namespace Speckle.Converter.Tekla2024;

[GenerateAutoInterface]
public class TeklaConversionSettingsFactory(
  IHostToSpeckleUnitConverter<TSD.Units> unitsConverter,
  IConverterSettingsStore<TeklaConversionSettings> settingsStore
) : ITeklaConversionSettingsFactory
{
  public TeklaConversionSettings Current => settingsStore.Current;

  // only handles automatic rn
  public TeklaConversionSettings Create(Model document) =>
    new(document, unitsConverter.ConvertOrThrow(TSD.Units.Automatic));
}