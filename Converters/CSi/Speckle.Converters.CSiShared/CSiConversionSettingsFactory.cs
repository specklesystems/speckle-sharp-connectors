using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.CSiShared;

[GenerateAutoInterface]
public class CSiConversionSettingsFactory(
  IHostToSpeckleUnitConverter<string> unitsConverter,
  IConverterSettingsStore<CSiConversionSettings> settingsStore
) : ICSiConversionSettingsFactory
{
  public CSiConversionSettings Current => settingsStore.Current;

  // TODO: Units currently hard-coded
  public CSiConversionSettings Create(cSapModel document) => new(document, unitsConverter.ConvertOrThrow("mm"));
}
