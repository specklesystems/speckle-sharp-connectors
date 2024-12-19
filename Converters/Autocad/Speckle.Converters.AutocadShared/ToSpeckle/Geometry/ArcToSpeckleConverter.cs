using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(nameof(ADB.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DBArcToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.Arc, SOG.Arc> _arcConverter;

  public DBArcToSpeckleConverter(ITypedConverter<ADB.Arc, SOG.Arc> arcConverter)
  {
    _arcConverter = arcConverter;
  }


  public BaseResult Convert(object target) => BaseResult.Success(Convert((ADB.Arc)target));
  public SOG.Arc Convert(ADB.Arc target) => _arcConverter.Convert(target);
}
