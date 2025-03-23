using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(ExtrusionObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ExtrusionObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<ExtrusionObject, RG.Extrusion, SOG.ExtrusionX>
{
  public ExtrusionObjectToSpeckleTopLevelConverter(ITypedConverter<RG.Extrusion, SOG.ExtrusionX> conversion)
    : base(conversion) { }

  protected override RG.Extrusion GetTypedGeometry(ExtrusionObject input) => input.ExtrusionGeometry;
}
