using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(HatchObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<HatchObject, RG.Hatch, SOG.Region>
{
  public HatchObjectToSpeckleTopLevelConverter(ITypedConverter<RG.Hatch, SOG.Region> conversion)
    : base(conversion) { }

  protected override RG.Hatch GetTypedGeometry(HatchObject input) => input.HatchGeometry;
}
