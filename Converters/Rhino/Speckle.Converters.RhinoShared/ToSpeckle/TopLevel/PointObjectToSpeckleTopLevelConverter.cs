using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(PointObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<PointObject, RG.Point, SOG.Point>
{
  public PointObjectToSpeckleTopLevelConverter(ITypedConverter<RG.Point, SOG.Point> conversion)
    : base(conversion) { }

  protected override RG.Point GetTypedGeometry(PointObject input) => input.PointGeometry;
}
