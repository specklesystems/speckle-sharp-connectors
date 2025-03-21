using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Arc, ACG.Polyline>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public ArcToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Arc)target);

  public ACG.Polyline Convert(SOG.Arc target)
  {
    if (target.startPoint.z != target.midPoint.z || target.startPoint.z != target.endPoint.z)
    {
      throw new ArgumentException("Only Arcs in XY plane are supported");
    }
    ACG.MapPoint fromPt = _pointConverter.Convert(target.startPoint);
    ACG.MapPoint toPt = _pointConverter.Convert(target.endPoint);
    ACG.MapPoint midPt = _pointConverter.Convert(target.midPoint);

    ACG.EllipticArcSegment segment = ACG.EllipticArcBuilderEx.CreateCircularArc(
      fromPt,
      toPt,
      new ACG.Coordinate2D(midPt),
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    );

    return new ACG.PolylineBuilderEx(
      segment,
      ACG.AttributeFlags.HasZ,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
