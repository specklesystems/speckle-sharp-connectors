using Rhino;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Grasshopper;

[GenerateAutoInterface]
public class GrasshopperConversionSettingsFactory(
  IHostToSpeckleUnitConverter<UnitSystem> unitsConverter,
  IConverterSettingsStore<GrasshopperConversionSettings> settingsStore
) : IGrasshopperConversionSettingsFactory
{
  public GrasshopperConversionSettings Current => settingsStore.Current;

  public GrasshopperConversionSettings Create(RhinoDoc document) =>
    new(document, unitsConverter.ConvertOrThrow(RhinoDoc.ActiveDoc.ModelUnitSystem));
}
