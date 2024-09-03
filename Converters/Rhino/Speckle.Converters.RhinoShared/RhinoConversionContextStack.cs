using Rhino;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Rhino;

public class RhinoConversionSettings : IConverterSettings
{
  public RhinoDoc Document { get; init; }
  public string SpeckleUnits { get; init; }
}

public partial interface IRhinoConversionSettingsFactory
{
  IDisposable Push(RhinoDoc? document = default, string? units = default);
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

  [AutoInterfaceIgnore]
  public IDisposable Push(RhinoDoc? document = null, string? units = null) =>
    settingsStore.Push(
      () =>
        new RhinoConversionSettings()
        {
          Document = document ?? settingsStore.Current.Document,
          SpeckleUnits = units ?? unitsConverter.ConvertOrThrow(RhinoDoc.ActiveDoc.ModelUnitSystem)
        }
    );
}
