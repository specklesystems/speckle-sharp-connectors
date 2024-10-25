using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(Beam), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BeamConverter(ITypedConverter<Beam, Base> beamConverter) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => beamConverter.Convert((Beam)target);
}
