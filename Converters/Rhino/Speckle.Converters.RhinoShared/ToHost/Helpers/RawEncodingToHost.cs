using Rhino.FileIO;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToHost.Helpers;

/// <summary>
/// Top level handler for raw encoded objects.
/// </summary>
public static class RawEncodingToHost
{
  public static List<RG.GeometryBase> Convert(SOG.IRawEncodedObject target)
  {
    // note: I am not sure that we're going to have other encoding formats, but who knows.
    switch (target.encodedValue.format)
    {
      case SO.RawEncodingFormats.RHINO_3DM:
        return Handle3dm(target);
      default:
        throw new ConversionException($"Unsupported brep encoding format: {target.encodedValue.format}");
    }
  }

  private static List<RG.GeometryBase> Handle3dm(SOG.IRawEncodedObject target)
  {
    var bytes = System.Convert.FromBase64String(target.encodedValue.contents);
    var file = File3dm.FromByteArray(bytes);
    var brepObject = file.Objects.Where(o => o.Geometry is not null).Select(o => o.Geometry);
    return brepObject.ToList();
  }
}
