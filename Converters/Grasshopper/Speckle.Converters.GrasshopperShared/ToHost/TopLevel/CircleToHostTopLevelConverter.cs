using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Circle, RG.Circle> _circleConverter;

  public CircleToHostTopLevelConverter(ITypedConverter<SOG.Circle, RG.Circle> circleConverter)
  {
    _circleConverter = circleConverter;
  }

  public object Convert(Base target) => Convert((SOG.Circle)target);

  public RG.Circle Convert(SOG.Circle target) => _circleConverter.Convert(target);
}
