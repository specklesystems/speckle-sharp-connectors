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

  public RhinoConversionSettings Create(RhinoDoc document) =>
    new(document, unitsConverter.ConvertOrThrow(RhinoDoc.ActiveDoc.ModelUnitSystem), ModelFarFromOrigin());

  /// <summary>
  /// Quick check whether any of the objects in the scene might be located too far from origin and cause precision issues during meshing.
  /// It prevents 'normal' Rhino models (not too far from origin) from unnecessary Bbox calculations on every object on Send.
  /// </summary>
  private bool ModelFarFromOrigin()
  {
    RG.BoundingBox bbox = RhinoDoc.ActiveDoc.Objects.BoundingBox;
    if (bbox.Min.DistanceTo(RG.Point3d.Origin) > 1e5 || bbox.Max.DistanceTo(RG.Point3d.Origin) > 1e5)
    {
      return true;
    }
    return false;
  }
}
