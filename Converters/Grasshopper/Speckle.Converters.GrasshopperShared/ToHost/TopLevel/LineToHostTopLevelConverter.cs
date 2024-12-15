using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Line), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Line, RG.Line> _lineConverter;

  public LineToHostTopLevelConverter(ITypedConverter<SOG.Line, RG.Line> lineConverter)
  {
    _lineConverter = lineConverter;
  }

  public object Convert(Base target) => Convert((SOG.Line)target);

  public RG.Line Convert(SOG.Line target) => _lineConverter.Convert(target);
}
