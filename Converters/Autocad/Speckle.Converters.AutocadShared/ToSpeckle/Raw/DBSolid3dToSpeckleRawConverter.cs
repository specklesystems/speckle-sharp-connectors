using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class Solid3dToSpeckleRawConverter : ITypedConverter<ADB.Solid3d, SOG.Mesh>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;

  public Solid3dToSpeckleRawConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  {
    _brepConverter = brepConverter;
  }

  public Base Convert(object target) => Convert((ADB.Solid3d)target);

  public SOG.Mesh Convert(ADB.Solid3d target)
  {
    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ValidationException("Could not retrieve brep from the solid3d.");
    }

    SOG.Mesh mesh = _brepConverter.Convert(brep);

    return mesh;
  }
}
