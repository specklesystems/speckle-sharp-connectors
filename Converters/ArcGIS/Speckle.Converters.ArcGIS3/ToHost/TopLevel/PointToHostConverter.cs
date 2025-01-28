using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(typeof(SOG.Point), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PointToHostConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _pointConverter;

  public PointToHostConverter(ITypedConverter<List<SOG.Point>, ACG.Multipoint> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public object Convert(Base target) => _pointConverter.Convert(new List<SOG.Point> { (SOG.Point)target });
}
