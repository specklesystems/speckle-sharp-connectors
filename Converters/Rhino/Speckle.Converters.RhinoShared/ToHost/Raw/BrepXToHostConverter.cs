using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToHost.Helpers;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class BrepXToHostConverter : ITypedConverter<SOG.BrepX, List<RG.GeometryBase>>
{
  public List<RG.GeometryBase> Convert(SOG.BrepX target) => RawEncodingToHost.Convert(target);
}

