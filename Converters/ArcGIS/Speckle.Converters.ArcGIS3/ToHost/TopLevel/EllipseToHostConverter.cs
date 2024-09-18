using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Ellipse), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EllipseToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Ellipse, ACG.Polyline>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public EllipseToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Ellipse)target);

  public ACG.Polyline Convert(SOG.Ellipse target)
  {
    if (
      target.plane.normal.x != 0
      || target.plane.normal.y != 0
      || target.plane.xdir.z != 0
      || target.plane.ydir.z != 0
    )
    {
      throw new ArgumentException("Only Ellipses in XY plane are supported");
    }

    ACG.MapPoint centerPt = _pointConverter.Convert(target.plane.origin);
    double scaleFactor = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);

    // set default values
    double angle =
      Math.Atan2(target.plane.xdir.y, target.plane.xdir.x)
      + _settingsStore.Current.ActiveCRSoffsetRotation.TrueNorthRadians;
    double majorAxisRadius = (double)target.firstRadius;
    double minorAxisRatio = (double)target.secondRadius / majorAxisRadius;

    // adjust if needed
    if (minorAxisRatio > 1)
    {
      majorAxisRadius = (double)target.secondRadius;
      minorAxisRatio = 1 / minorAxisRatio;
      angle += Math.PI / 2;
    }

    ACG.EllipticArcSegment segment = ACG.EllipticArcBuilderEx.CreateEllipse(
      new ACG.Coordinate2D(centerPt),
      angle,
      majorAxisRadius * scaleFactor,
      minorAxisRatio,
      ACG.ArcOrientation.ArcCounterClockwise,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    );

    return new ACG.PolylineBuilderEx(
      segment,
      ACG.AttributeFlags.HasZ,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
