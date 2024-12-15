using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Curve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CurveToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Curve, RG.NurbsCurve> _curveConverter;

  public CurveToHostTopLevelConverter(ITypedConverter<SOG.Curve, RG.NurbsCurve> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public object Convert(Base target) => Convert((SOG.Curve)target);

  public RG.NurbsCurve Convert(SOG.Curve target) => _curveConverter.Convert(target);
}
