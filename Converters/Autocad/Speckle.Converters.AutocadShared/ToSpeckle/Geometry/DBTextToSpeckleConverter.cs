using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(typeof(ADB.DBText), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DBTextToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ADB.DBText, SA.Text> _textConverter;

  public DBTextToSpeckleConverter(ITypedConverter<ADB.DBText, SA.Text> textConverter)
  {
    _textConverter = textConverter;
  }

  public Base Convert(object target) => Convert((ADB.DBText)target);

  public SA.Text Convert(ADB.DBText target) => _textConverter.Convert(target);
}
