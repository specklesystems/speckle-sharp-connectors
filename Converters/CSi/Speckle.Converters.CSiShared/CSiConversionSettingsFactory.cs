using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.CSiShared;

[GenerateAutoInterface]
public class CSiConversionSettingsFactory(
  IHostToSpeckleUnitConverter<eUnits> unitsConverter,
  IConverterSettingsStore<CSiConversionSettings> settingsStore
) : ICSiConversionSettingsFactory
{
  public CSiConversionSettings Current => settingsStore.Current;

  public CSiConversionSettings Create(cSapModel document) =>
    new(document, unitsConverter.ConvertOrThrow(document.GetPresentUnits()));
}
