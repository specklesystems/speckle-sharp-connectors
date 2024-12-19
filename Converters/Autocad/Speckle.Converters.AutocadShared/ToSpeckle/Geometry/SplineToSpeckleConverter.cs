using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(nameof(ADB.Spline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SplineToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Spline, SOG.Curve> _splineConverter;

  public SplineToSpeckleConverter(ITypedConverter<ADB.Spline, SOG.Curve> splineConverter)
  {
    _splineConverter = splineConverter;
  }

  public BaseResult Convert(object target) => BaseResult.Success(Convert((ADB.Spline)target));

  public SOG.Curve Convert(ADB.Spline target) => _splineConverter.Convert(target);
}
