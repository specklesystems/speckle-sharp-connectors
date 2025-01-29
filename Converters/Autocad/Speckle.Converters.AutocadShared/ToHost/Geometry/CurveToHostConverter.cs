using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.AutocadShared.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Curve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CurveToHostConverter(ITypedConverter<SOG.Curve, AG.NurbCurve3d> curveConverter)
  : ITypedConverter<SOG.Curve, ADB.Curve>
{
  public Result<ADB.Curve> Convert(SOG.Curve target)
  {
    if (!curveConverter.Try(target, out Result<AG.NurbCurve3d> normal))
    {
      return normal.Failure<ADB.Curve>();
    }

    return Success(ADB.Curve.CreateFromGeCurve(normal.Value));
  }
}
