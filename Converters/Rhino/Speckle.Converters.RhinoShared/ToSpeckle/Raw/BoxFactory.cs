using System.Diagnostics.CodeAnalysis;
using Speckle.InterfaceGenerator;
using BoundingBox = Rhino.Geometry.BoundingBox;
using Box = Rhino.Geometry.Box;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

[GenerateAutoInterface]
[ExcludeFromCodeCoverage]
public class BoxFactory : IBoxFactory
{
  public Box Create(BoundingBox bb) => new(bb);
}
