using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(BrepObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<BrepObject, RG.Brep, SOG.BrepX>
{
  public BrepObjectToSpeckleTopLevelConverter(ITypedConverter<RG.Brep, SOG.BrepX> conversion)
    : base(conversion) { }

  protected override RG.Brep GetTypedGeometry(BrepObject input) => input.BrepGeometry;
}
