using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(ADB.Surface), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SurfaceToSpeckleConverter :  ITypedConverter<ADB.Surface, SOG.Mesh>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;

  public SurfaceToSpeckleConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  {
    _brepConverter = brepConverter;
  }

  public Base Convert(object target) => Convert((ADB.Surface)target);

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
