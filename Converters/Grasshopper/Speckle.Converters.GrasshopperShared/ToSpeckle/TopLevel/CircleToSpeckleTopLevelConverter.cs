using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Circle, SOG.Circle> _lineConverter;

  public CircleToSpeckleTopLevelConverter(ITypedConverter<RG.Circle, SOG.Circle> lineConverter)
  {
    _lineConverter = lineConverter;
  }

  public Base Convert(object target) => Convert((RG.Circle)target);

  public Base Convert(RG.Circle target) => _lineConverter.Convert(target);
}
