using Speckle.Converters.Common.Objects;
using Speckle.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class NurbCurve3dToSpeckleConverter : ITypedConverter<AG.NurbCurve3d, ICurve>
{
  private readonly ITypedConverter<ADB.Curve, ICurve> _curveConverter;

  public NurbCurve3dToSpeckleConverter(ITypedConverter<ADB.Curve, ICurve> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public ICurve Convert(AG.NurbCurve3d target) => _curveConverter.Convert(ADB.Curve.CreateFromGeCurve(target));
}
