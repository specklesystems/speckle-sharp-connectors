using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SerializationOptions : ISerializationOptions
{
  public bool SkipCacheRead { get; set; }
  public bool SkipCacheWrite { get; set; }
  public bool SkipServer { get; set; }
  public bool SkipFindTotalObjects { get; set; }
}
