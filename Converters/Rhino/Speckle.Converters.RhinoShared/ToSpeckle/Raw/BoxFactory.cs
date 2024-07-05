using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Rhino7.ToSpeckle.Raw;

[GenerateAutoInterface]
[ExcludeFromCodeCoverage]
public class BoxFactory : IBoxFactory
{
  public RG.Box Create(RG.BoundingBox bb) => new(bb);
}
