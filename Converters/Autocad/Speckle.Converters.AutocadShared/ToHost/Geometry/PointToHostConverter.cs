using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Point), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointToHostConverter : ITypedConverter<SOG.Point, ADB.DBPoint>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;

  public PointToHostConverter(ITypedConverter<SOG.Point, AG.Point3d> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public ADB.DBPoint Convert(SOG.Point target) => new(_pointConverter.Convert(target));
}
