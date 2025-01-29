using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToHost.Helpers;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class ExtrusionXToHostConverter : ITypedConverter<SOG.ExtrusionX, List<RG.GeometryBase>>
{
  public List<RG.GeometryBase> Convert(SOG.ExtrusionX target) => RawEncodingToHost.Convert(target);
}
