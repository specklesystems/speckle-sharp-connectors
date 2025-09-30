using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class PlaneToSpeckleRawConverter : ITypedConverter<AG.Plane, SOG.Plane>
{
  private readonly ITypedConverter<AG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public PlaneToSpeckleRawConverter(
    ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _vectorConverter = vectorConverter;
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((AG.Plane)target);

  public SOG.Plane Convert(AG.Plane target) =>
    new()
    {
      origin = _pointConverter.Convert(target.PointOnPlane),
      normal = _vectorConverter.Convert(target.Normal),
      xdir = _vectorConverter.Convert(target.GetCoordinateSystem().Xaxis), // TODO: validate if this returns the coordinate system in GCS or already transformed
      ydir = _vectorConverter.Convert(target.GetCoordinateSystem().Yaxis), // TODO: validate if this returns the coordinate system in GCS or already transformed
      units = _settingsStore.Current.SpeckleUnits,
    };
}
