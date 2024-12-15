using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Arc, ICurve> _arcConverter;

  public ArcToSpeckleTopLevelConverter(ITypedConverter<RG.Arc, ICurve> arcConverter)
  {
    _arcConverter = arcConverter;
  }

  public Base Convert(object target) => Convert((RG.Arc)target);

  public SOG.Arc Convert(RG.Arc target) => (SOG.Arc)_arcConverter.Convert(target);
}
