using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(nameof(ADB.DBPoint), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;

  public PointToSpeckleConverter(ITypedConverter<AG.Point3d, SOG.Point> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public BaseResult Convert(object target) => BaseResult.Success(RawConvert((ADB.DBPoint)target));

  public SOG.Point RawConvert(ADB.DBPoint target) => _pointConverter.Convert(target.Position);
}
