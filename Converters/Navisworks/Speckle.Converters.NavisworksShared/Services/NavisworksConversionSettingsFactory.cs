using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.RevitShared.Settings;

[GenerateAutoInterface]
public class NavisworksConversionSettingsFactory(
  IHostToSpeckleUnitConverter<NAV.Units> unitsConverter,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore
) : INavisworksConversionSettingsFactory
{
  public NavisworksConversionSettings Current => settingsStore.Current;

  public NavisworksConversionSettings Create(NAV.Document document) =>
    new(document, unitsConverter.ConvertOrThrow(NavisworksApp.ActiveDocument.Units));
}
