using ArcGIS.Core.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class EnvelopToSpeckleConverter : ITypedConverter<Envelope, SOG.Box>
{
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;
  private readonly ITypedConverter<MapPoint, SOG.Point> _pointConverter;

  public EnvelopToSpeckleConverter(
    IConversionContextStack<ArcGISDocument, Unit> contextStack,
    ITypedConverter<MapPoint, SOG.Point> pointConverter
  )
  {
    _contextStack = contextStack;
    _pointConverter = pointConverter;
  }

  public SOG.Box Convert(Envelope target)
  {
    MapPoint pointMin = new MapPointBuilderEx(
      target.XMin,
      target.YMin,
      target.ZMin,
      _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
    MapPoint pointMax = new MapPointBuilderEx(
      target.XMax,
      target.YMax,
      target.ZMax,
      _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
    SOG.Point minPtSpeckle = _pointConverter.Convert(pointMin);
    SOG.Point maxPtSpeckle = _pointConverter.Convert(pointMax);

    var units = _contextStack.Current.SpeckleUnits;

    SOG.Plane plane =
      new()
      {
        origin = minPtSpeckle,
        normal = new SOG.Vector(0, 0, 1, units),
        xdir = new SOG.Vector(1, 0, 0, units),
        ydir = new SOG.Vector(0, 1, 0, units),
        units = units
      };

    return new SOG.Box()
    {
      basePlane = plane,
      xSize = new Interval { start = minPtSpeckle.x, end = maxPtSpeckle.x },
      ySize = new Interval { start = minPtSpeckle.y, end = maxPtSpeckle.y },
      zSize = new Interval { start = minPtSpeckle.z, end = maxPtSpeckle.z },
      units = _contextStack.Current.SpeckleUnits
    };
  }
}
