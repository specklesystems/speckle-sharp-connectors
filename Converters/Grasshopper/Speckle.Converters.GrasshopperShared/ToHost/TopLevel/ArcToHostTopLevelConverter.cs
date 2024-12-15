using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Arc, RG.Arc> _arcConverter;

  public ArcToHostTopLevelConverter(ITypedConverter<SOG.Arc, RG.Arc> arcConverter)
  {
    _arcConverter = arcConverter;
  }

  public object Convert(Base target) => Convert((SOG.Arc)target);

  public RG.Arc Convert(SOG.Arc target) => _arcConverter.Convert(target);
}
