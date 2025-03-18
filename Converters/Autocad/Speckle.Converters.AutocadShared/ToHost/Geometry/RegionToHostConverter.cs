using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Region), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RegionToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Region, ADB.Entity>
{
  private readonly ITypedConverter<SOG.Region, ADB.Region> _regionConverter;

  public RegionToHostConverter(ITypedConverter<SOG.Region, ADB.Region> regionConverter)
  {
    _regionConverter = regionConverter;
  }

  public object Convert(Base target) => Convert((SOG.Region)target);

  public ADB.Entity Convert(SOG.Region target)
  {
    // Generalizing return type as Entity, because it can be a simple Region, or a Hatch
    // later to be differentiated via (target.hasHatchPattern). For now, just convert to Region
    return _regionConverter.Convert(target);
  }
}
