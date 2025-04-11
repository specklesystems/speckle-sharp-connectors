using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(TextObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextObjectToSpeckleTopLevelConverter
  : RhinoObjectToSpeckleTopLevelConverter<TextObject, RG.TextEntity, SO.Text>
{
  public TextObjectToSpeckleTopLevelConverter(ITypedConverter<RG.TextEntity, SO.Text> conversion)
    : base(conversion) { }

  protected override RG.TextEntity GetTypedGeometry(TextObject input) => input.TextGeometry;
}
