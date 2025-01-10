using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Circle, ACG.Polyline>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public CircleToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Circle)target);

  public ACG.Polyline Convert(SOG.Circle target)
  {
    if (
      target.plane.normal.x != 0
      || target.plane.normal.y != 0
      || target.plane.xdir.z != 0
      || target.plane.ydir.z != 0
    )
    {
      throw new ArgumentException("Only Circles in XY plane are supported");
    }

    // create a native ArcGIS circle segment
    ACG.MapPoint centerPt = _pointConverter.Convert(target.plane.origin);

    double scaleFactor = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    ACG.EllipticArcSegment circleSegment = ACG.EllipticArcBuilderEx.CreateCircle(
      new ACG.Coordinate2D(centerPt.X, centerPt.Y),
      (double)target.radius * scaleFactor,
      ACG.ArcOrientation.ArcClockwise,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    );

    return new ACG.PolylineBuilderEx(
      circleSegment,
      ACG.AttributeFlags.HasZ,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
