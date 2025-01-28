using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToHost.Raw;

[NameAndRankValue(typeof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostRowConverter : ITypedConverter<SOG.Arc, AG.CircularArc3d>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly ITypedConverter<SOG.Vector, AG.Vector3d> _vectorConverter;

  public ArcToHostRowConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  public AG.CircularArc3d Convert(SOG.Arc target)
  {
    AG.Point3d start = _pointConverter.Convert(target.startPoint);
    AG.Point3d end = _pointConverter.Convert(target.endPoint);
    AG.Point3d mid = _pointConverter.Convert(target.midPoint);
    AG.CircularArc3d arc = new(start, mid, end);
    return arc;
  }
}
