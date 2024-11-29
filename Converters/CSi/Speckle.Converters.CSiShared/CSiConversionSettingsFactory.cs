using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.TeklaShared;

[GenerateAutoInterface]
public class TeklaConversionSettingsFactory(
  IHostToSpeckleUnitConverter<string> unitsConverter,
  IConverterSettingsStore<CSiConversionSettings> settingsStore
) : ITeklaConversionSettingsFactory
{
  public CSiConversionSettings Current => settingsStore.Current;

  // NOTE: Distance.CurrentUnitType reflects Settings > Options > Units and decimals
  // Internal units (mm) are, however, always returned.
  // If model units != internal units, user can rely on units appended to each report parameter
  public CSiConversionSettings Create(Model document, bool sendRebarsAsSolid) =>
    new(document, sendRebarsAsSolid, unitsConverter.ConvertOrThrow(Distance.CurrentUnitType));
}
