using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Circle, SOG.Circle> _circleConverter;

  public CircleToSpeckleTopLevelConverter(ITypedConverter<RG.Circle, SOG.Circle> circleConverter)
  {
    _circleConverter = circleConverter;
  }

  public Base Convert(object target) => Convert((RG.Circle)target);

  public Base Convert(RG.Circle target) => _circleConverter.Convert(target);
}
