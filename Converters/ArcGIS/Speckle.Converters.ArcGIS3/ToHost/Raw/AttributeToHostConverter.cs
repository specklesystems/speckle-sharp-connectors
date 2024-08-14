using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class AttributeToHostConverter : ITypedConverter<Base, Dictionary<string, object?>>
{
  public AttributeToHostConverter() { }

  public Dictionary<string, object?> Convert(Base target)
  {
    return target.GetMembers(DynamicBaseMemberType.Dynamic);
  }
}
