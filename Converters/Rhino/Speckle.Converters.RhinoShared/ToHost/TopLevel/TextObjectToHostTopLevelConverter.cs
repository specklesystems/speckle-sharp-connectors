using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino;

namespace Speckle.Converter.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SA.Text), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]

public class TextObjectToHostTopLevelConverter
  : SpeckleToHostGeometryBaseTopLevelConverter
  
  <TextObject, RG.TextEntity, SA.Text>
{
  public TextObjectToHostTopLevelConverter(
    ITypedConverter<SA.Text, RG.TextEntity> conversion
  ) : base(conversion) { }
  protected override RG.TextEntity GetTypedGeometry(TextObject input) => input.TextGeometry;
}
