using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBBodyToSpeckleRawConverter : ITypedConverter<ADB.Body, SOG.Mesh>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;

  public DBBodyToSpeckleRawConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  {
    _brepConverter = brepConverter;
  }

  public Base Convert(object target) => Convert((ADB.Body)target);

  public SOG.Mesh Convert(ADB.Body target)
  {
    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ConversionException("Could not retrieve brep from the body.");
    }

    return _brepConverter.Convert(brep);
  }
}
