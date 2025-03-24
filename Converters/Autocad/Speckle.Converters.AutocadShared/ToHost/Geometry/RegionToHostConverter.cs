using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Region), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RegionToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Region, ADB.Entity>
{
  private readonly ITypedConverter<SOG.Region, ADB.Region> _regionConverter;
  private readonly ITypedConverter<SOG.Region, ADB.Hatch> _hatchConverter;

  public RegionToHostConverter(
    ITypedConverter<SOG.Region, ADB.Region> regionConverter,
    ITypedConverter<SOG.Region, ADB.Hatch> hatchConverter
  )
  {
    _regionConverter = regionConverter;
    _hatchConverter = hatchConverter;
  }

  public object Convert(Base target) => Convert((SOG.Region)target);

  public ADB.Entity Convert(SOG.Region target)
  {
    // Generalizing return type as Entity, because it can be a simple Region, or a Hatch
    if (target.hasHatchPattern)
    {
      return _hatchConverter.Convert(target);
    }
    return _regionConverter.Convert(target);
  }
}
