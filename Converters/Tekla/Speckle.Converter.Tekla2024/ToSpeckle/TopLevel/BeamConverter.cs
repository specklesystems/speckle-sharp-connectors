using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.Beam), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BeamConverter(ITypedConverter<TSM.Beam, Base> beamConverter) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => beamConverter.Convert((TSM.Beam)target);
}
