using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToHost.Helpers;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class SubDXToHostConverter : ITypedConverter<SOG.SubDX, List<RG.GeometryBase>>
{
  public List<RG.GeometryBase> Convert(SOG.SubDX target) => RawEncodingToHost.Convert(target);
}
