using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(nameof(ADB.Surface), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SurfaceToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Surface, SOG.Mesh>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;

  public SurfaceToSpeckleConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  {
    _brepConverter = brepConverter;
  }

  public BaseResult Convert(object target) => BaseResult.Success(Convert((ADB.Surface)target));

  public SOG.Mesh Convert(ADB.Surface target)
  {
    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ConversionException("Could not retrieve brep from the plane surface.");
    }

    return _brepConverter.Convert(brep);
  }
}
