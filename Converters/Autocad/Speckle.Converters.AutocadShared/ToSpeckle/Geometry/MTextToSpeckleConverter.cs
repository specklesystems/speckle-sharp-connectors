using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(typeof(ADB.MText), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MTextToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.MText, SA.Text> _textConverter;

  public MTextToSpeckleConverter(ITypedConverter<ADB.MText, SA.Text> textConverter)
  {
    _textConverter = textConverter;
  }

  public Base Convert(object target) => Convert((ADB.MText)target);

  public SA.Text Convert(ADB.MText target) => _textConverter.Convert(target);
}
