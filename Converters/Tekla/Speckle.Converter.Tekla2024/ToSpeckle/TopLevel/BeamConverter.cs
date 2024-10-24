using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024.ToSpeckle;

[NameAndRankValue(nameof(Beam), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BeamConverter: IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<Beam, Base> _beamConverter;

  public BeamConverter(ITypedConverter<Beam, Base> beamConverter)
  {
    _beamConverter = beamConverter;
  }

  public Base Convert(object target) => _beamConverter.Convert((Beam)target); 
}
