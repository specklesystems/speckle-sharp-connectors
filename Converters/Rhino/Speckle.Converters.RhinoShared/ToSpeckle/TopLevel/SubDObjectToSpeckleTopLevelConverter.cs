using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(SubDObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SubDObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<SubDObject, RG.SubD, SOG.SubDX>
{
  public SubDObjectToSpeckleTopLevelConverter(ITypedConverter<RG.SubD, SOG.SubDX> conversion)
    : base(conversion) { }

  protected override RG.SubD GetTypedGeometry(SubDObject input) => (RG.SubD)input.Geometry;
}
