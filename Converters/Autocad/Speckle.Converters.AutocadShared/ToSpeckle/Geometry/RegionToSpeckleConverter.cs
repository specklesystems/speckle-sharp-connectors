using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(nameof(ADB.Region), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RegionToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Region, SOG.Mesh>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;

  public RegionToSpeckleConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  {
    _brepConverter = brepConverter;
  }

  public BaseResult Convert(object target) => BaseResult.Success(Convert((ADB.Region)target));

  public SOG.Mesh Convert(ADB.Region target)
  {
    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ConversionException("Could not retrieve brep from the region.");
    }

    SOG.Mesh mesh = _brepConverter.Convert(brep);
    mesh.area = target.Area;

    return mesh;
  }
}
