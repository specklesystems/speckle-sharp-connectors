using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class Point3dCollectionToSpeckleRawConverter : ITypedConverter<AG.Point3dCollection, SOG.Polyline>
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public Point3dCollectionToSpeckleRawConverter(
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Polyline Convert(object target) => Convert((AG.Point3dCollection)target);

  public SOG.Polyline Convert(AG.Point3dCollection target)
  {
    List<double> value = new();
    double length = 0;
    AG.Point3d? previousPoint = null;
    foreach (AG.Point3d point in target)
    {
      value.Add(point.X);
      value.Add(point.Y);
      value.Add(point.Z);
      if (previousPoint is AG.Point3d p)
      {
        length += point.DistanceTo(p);
      }
      previousPoint = point;
    }

    return new()
    {
      value = _referencePointConverter.ConvertWCSDoublesToExternalCoordinates(value), // transform by reference point
      units = _settingsStore.Current.SpeckleUnits,
      closed = false,
      length = length
    };
  }
}
