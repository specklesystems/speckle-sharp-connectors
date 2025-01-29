using Speckle.Converters.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBBodyToSpeckleRawConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  : ITypedConverter<ADB.Body, SOG.Mesh>
{
  public Base Convert(object target) => Convert((ADB.Body)target).Value;

  public Result<SOG.Mesh> Convert(ADB.Body target)
  {
    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ConversionException("Could not retrieve brep from the body.");
    }

    return brepConverter.Convert(brep);
  }
}
