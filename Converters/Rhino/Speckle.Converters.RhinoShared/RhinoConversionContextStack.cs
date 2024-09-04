using Rhino;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Rhino;

public record RhinoConversionSettings
{
  public RhinoDoc Document { get; init; }
  public string SpeckleUnits { get; init; }
}

[GenerateAutoInterface]
public class RhinoConversionSettingsFactory(
  IHostToSpeckleUnitConverter<UnitSystem> unitsConverter,
  IConverterSettingsStore<RhinoConversionSettings> settingsStore
) : IRhinoConversionSettingsFactory
{
  public RhinoConversionSettings Current => settingsStore.Current;

  public RhinoConversionSettings Create(RhinoDoc document) =>
    new() { Document = document, SpeckleUnits = unitsConverter.ConvertOrThrow(RhinoDoc.ActiveDoc.ModelUnitSystem) };
}
