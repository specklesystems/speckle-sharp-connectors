using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Point3d), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class Point3dToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;

  public Point3dToSpeckleTopLevelConverter(ITypedConverter<RG.Point3d, SOG.Point> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public Base Convert(object target) => Convert((RG.Point3d)target);

  public SOG.Point Convert(RG.Point3d target) => _pointConverter.Convert(target);
}
