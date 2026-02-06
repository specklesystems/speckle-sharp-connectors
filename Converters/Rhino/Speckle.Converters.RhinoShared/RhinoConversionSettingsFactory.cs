using Rhino;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Rhino;

[GenerateAutoInterface]
public class RhinoConversionSettingsFactory(
  IHostToSpeckleUnitConverter<UnitSystem> unitsConverter,
  IConverterSettingsStore<RhinoConversionSettings> settingsStore
) : IRhinoConversionSettingsFactory
{
  public RhinoConversionSettings Current => settingsStore.Current;

  public RhinoConversionSettings Create(RhinoDoc document, bool addVisualizationProperties) =>
    new(document, unitsConverter.ConvertOrThrow(RhinoDoc.ActiveDoc.ModelUnitSystem), addVisualizationProperties);

  public RhinoConversionSettings Create(
    RhinoDoc document,
    bool addVisualizationProperties,
    bool convertMeshesToBreps
  ) =>
    new(
      document,
      unitsConverter.ConvertOrThrow(RhinoDoc.ActiveDoc.ModelUnitSystem),
      addVisualizationProperties,
      convertMeshesToBreps
    );
}
