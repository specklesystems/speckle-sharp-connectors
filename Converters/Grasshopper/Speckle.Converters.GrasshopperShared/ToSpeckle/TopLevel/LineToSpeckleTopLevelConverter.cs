using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Line), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Line, SOG.Line> _lineConverter;

  public LineToSpeckleTopLevelConverter(ITypedConverter<RG.Line, SOG.Line> lineConverter)
  {
    _lineConverter = lineConverter;
  }

  public Base Convert(object target) => Convert((RG.Line)target);

  public SOG.Line Convert(RG.Line target) => _lineConverter.Convert(target);
}
