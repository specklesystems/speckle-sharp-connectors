using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Core.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Arc, ACG.Polyline>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public ArcToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack
  )
  {
    _pointConverter = pointConverter;
    _contextStack = contextStack;
  }

  public object Convert(Base target) => Convert((SOG.Arc)target);

  public ACG.Polyline Convert(SOG.Arc target)
  {
    if (target.startPoint.z != target.midPoint.z || target.startPoint.z != target.endPoint.z)
    {
      throw new ArgumentException("Only Arc in XY plane are supported");
    }
    ACG.MapPoint fromPt = _pointConverter.Convert(target.startPoint);
    ACG.MapPoint toPt = _pointConverter.Convert(target.endPoint);
    ACG.MapPoint midPt = _pointConverter.Convert(target.midPoint);

    ACG.EllipticArcSegment segment = ACG.EllipticArcBuilderEx.CreateCircularArc(
      fromPt,
      toPt,
      new ACG.Coordinate2D(midPt),
      _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference
    );

    return new ACG.PolylineBuilderEx(
      segment,
      ACG.AttributeFlags.HasZ,
      _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
