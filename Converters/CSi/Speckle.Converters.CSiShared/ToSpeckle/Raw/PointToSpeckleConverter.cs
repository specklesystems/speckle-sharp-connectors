using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

public class PointToSpeckleConverter : ITypedConverter<CSiPointWrapper, Point>
{
  public Point Convert(CSiPointWrapper target) => throw new NotImplementedException();
}
