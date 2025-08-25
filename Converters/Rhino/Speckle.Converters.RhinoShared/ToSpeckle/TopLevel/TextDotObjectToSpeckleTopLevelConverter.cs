using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(TextDotObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextDotObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<TextDotObject, RG.TextDot, SA.Text>
{
  public TextDotObjectToSpeckleTopLevelConverter(ITypedConverter<RG.TextDot, SA.Text> conversion)
    : base(conversion) { }

  protected override RG.TextDot GetTypedGeometry(TextDotObject input) => (RG.TextDot)input.Geometry;
}
