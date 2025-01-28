using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.AutocadShared.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Curve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CurveToHostConverter : ITypedConverter<SOG.Curve, ADB.Curve>
{
  private readonly ITypedConverter<SOG.Curve, AG.NurbCurve3d> _curveConverter;

  public CurveToHostConverter(ITypedConverter<SOG.Curve, AG.NurbCurve3d> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public ADB.Curve Convert(SOG.Curve target) => ADB.Curve.CreateFromGeCurve(_curveConverter.Convert(target));
}
