using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class PlaneToHostRawConverter : ITypedConverter<SOG.Plane, AG.Plane>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly ITypedConverter<SOG.Vector, AG.Vector3d> _vectorConverter;

  public PlaneToHostRawConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  public AG.Plane Convert(SOG.Plane target) =>
    new(_pointConverter.Convert(target.origin), _vectorConverter.Convert(target.normal));
}
