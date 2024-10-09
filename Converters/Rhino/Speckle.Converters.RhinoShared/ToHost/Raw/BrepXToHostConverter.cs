using Rhino.FileIO;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class BrepXToHostConverter : ITypedConverter<SOG.BrepX, List<RG.GeometryBase>>
{
  public List<RG.GeometryBase> Convert(SOG.BrepX target) => RawEncodingToHost.Convert(target);
}

public class SubDXToHostConverter : ITypedConverter<SOG.SubDX, List<RG.GeometryBase>>
{
  public List<RG.GeometryBase> Convert(SOG.SubDX target) => RawEncodingToHost.Convert(target);
}

public class ExtrusionElonMuskXToHostConverter : ITypedConverter<SOG.ExtrusionX, List<RG.GeometryBase>>
{
  public List<RG.GeometryBase> Convert(SOG.ExtrusionX target) => RawEncodingToHost.Convert(target);
}

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
        throw new SpeckleConversionException($"Unsupported brep encoding format: {target.encodedValue.format}");
    }
  }

  private static List<RG.GeometryBase> Handle3dm(SOG.IRawEncodedObject target)
  {
    var bytes = System.Convert.FromBase64String(target.encodedValue.contents);
    var file = File3dm.FromByteArray(bytes);
    var brepObject = file.Objects.Select(o => o.Geometry);
    return brepObject.ToList();
  }
}
