using Rhino.FileIO;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToHost.Helpers;

/// <summary>
/// Top level handler for raw encoded objects.
/// </summary>
public static class RawEncodingToHost
{
  public static List<RG.GeometryBase> Convert(SOG.IRawEncodedObject target) => Convert(target.encodedValue);

  public static List<RG.GeometryBase> Convert(SO.RawEncoding encoding)
  {
    // note: I am not sure that we're going to have other encoding formats, but who knows.
    switch (encoding.format)
    {
      case SO.RawEncodingFormats.RHINO_3DM:
        return Handle3dm(encoding);
      default:
        throw new ConversionException($"Unsupported brep encoding format: {encoding.format}");
    }
  }

  private static List<RG.GeometryBase> Handle3dm(SO.RawEncoding encoding) =>
    Convert3dm(System.Convert.FromBase64String(encoding.contents));

  /// <summary>Decodes a raw 3dm byte blob straight to Rhino geometry — no <c>RawEncoding</c>/base64 round-trip. Used by
  /// the Speckle 4.0 artefact host builder, which reads the parquet SOLID blob as bytes.</summary>
  public static List<RG.GeometryBase> Convert3dm(byte[] bytes)
  {
    var file = File3dm.FromByteArray(bytes);
    return file.Objects.Where(o => o.Geometry is not null).Select(o => o.Geometry).ToList();
  }
}
