using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RG.Polyline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolylineToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Polyline, SOG.Polyline> _lineConverter;

  public PolylineToSpeckleTopLevelConverter(ITypedConverter<RG.Polyline, SOG.Polyline> lineConverter)
  {
    _lineConverter = lineConverter;
  }

  public Base Convert(object target) => Convert((RG.Polyline)target);

  public Base Convert(RG.Polyline target) => _lineConverter.Convert(target);
}
