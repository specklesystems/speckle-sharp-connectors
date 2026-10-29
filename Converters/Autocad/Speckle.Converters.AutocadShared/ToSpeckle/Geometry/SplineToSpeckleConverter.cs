using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(typeof(ADB.Spline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SplineToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Spline, SOG.Curve> _splineConverter;
  private readonly ITypedConverter<ADB.Spline, SOG.Polyline> _displayValueConverter;

  public SplineToSpeckleConverter(
    ITypedConverter<ADB.Spline, SOG.Curve> splineConverter,
    ITypedConverter<ADB.Spline, SOG.Polyline> displayValueConverter
  )
  {
    _splineConverter = splineConverter;
    _displayValueConverter = displayValueConverter;
  }

  public Base Convert(object target) => Convert((ADB.Spline)target);

  public SOG.Curve Convert(ADB.Spline target)
  {
    var curve = _splineConverter.Convert(target);
    curve.displayValue ??= _displayValueConverter.Convert(target);
    return curve;
  }
}
