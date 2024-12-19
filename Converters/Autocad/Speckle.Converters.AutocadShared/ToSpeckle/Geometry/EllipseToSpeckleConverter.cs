using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(nameof(ADB.Ellipse), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DBEllipseToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Ellipse, SOG.Ellipse> _ellipseConverter;

  public DBEllipseToSpeckleConverter(ITypedConverter<ADB.Ellipse, SOG.Ellipse> ellipseConverter)
  {
    _ellipseConverter = ellipseConverter;
  }

  public BaseResult Convert(object target) => BaseResult.Success(RawConvert((ADB.Ellipse)target));

  public SOG.Ellipse RawConvert(ADB.Ellipse target) => _ellipseConverter.Convert(target);
}
