using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Polyline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolylineToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Polyline, RG.Polyline> _polylineConverter;

  public PolylineToHostTopLevelConverter(ITypedConverter<SOG.Polyline, RG.Polyline> polylineConverter)
  {
    _polylineConverter = polylineConverter;
  }

  public object Convert(Base target) => Convert((SOG.Polyline)target);

  public RG.Polyline Convert(SOG.Polyline target) => _polylineConverter.Convert(target);
}
