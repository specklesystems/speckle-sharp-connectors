using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.CSiShared;

[GenerateAutoInterface]
public class CsiConversionSettingsFactory(
  IHostToSpeckleUnitConverter<eUnits> unitsConverter,
  IConverterSettingsStore<CsiConversionSettings> settingsStore
) : ICsiConversionSettingsFactory
{
  public CsiConversionSettings Current => settingsStore.Current;

  public CsiConversionSettings Create(cSapModel document) =>
    new(document, unitsConverter.ConvertOrThrow(document.GetPresentUnits()));
}
