using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RhinoShared.ToHost.Grasshopper;

[NameAndRankValue(typeof(Speckle.Objects.Primitive.Interval), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class IntervalToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<Speckle.Objects.Primitive.Interval, RG.Interval> _intervalConverter;

  public IntervalToHostTopLevelConverter(
    ITypedConverter<Speckle.Objects.Primitive.Interval, RG.Interval> intervalConverter
  )
  {
    _intervalConverter = intervalConverter;
  }

  public object Convert(Base target) => Convert((Speckle.Objects.Primitive.Interval)target);

  public RG.Interval Convert(Speckle.Objects.Primitive.Interval target)
  {
    return _intervalConverter.Convert(target);
  }
}
