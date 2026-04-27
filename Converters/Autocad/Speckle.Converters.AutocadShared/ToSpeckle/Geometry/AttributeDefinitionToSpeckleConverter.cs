using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

// AttributeDefinition inherits from DBText. Without this converter, ATTDEF entities
// falls back to DBTextToSpeckleConverter which reads `TextString`
// The default attribute value, typically empty for a ATTDEF
// The visible text in the drawing is the Tag, so we use that instead.
[NameAndRankValue(typeof(ADB.AttributeDefinition), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class AttributeDefinitionToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.DBText, SA.Text> _textConverter;

  public AttributeDefinitionToSpeckleConverter(ITypedConverter<ADB.DBText, SA.Text> textConverter)
  {
    _textConverter = textConverter;
  }

  public Base Convert(object target) => Convert((ADB.AttributeDefinition)target);

  public SA.Text Convert(ADB.AttributeDefinition target)
  {
    SA.Text result = _textConverter.Convert(target);
    result.value = !string.IsNullOrEmpty(target.Tag) ? target.Tag : target.TextString;
    return result;
  }
}
