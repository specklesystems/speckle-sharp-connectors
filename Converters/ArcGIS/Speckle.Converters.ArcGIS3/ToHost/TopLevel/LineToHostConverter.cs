using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Line), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LineSingleToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Line, ACG.Polyline>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public LineSingleToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public HostResult Convert(Base target) => HostResult.Success( Convert((SOG.Line)target));

  public ACG.Polyline Convert(SOG.Line target)
  {
    List<SOG.Point> originalPoints = new() { target.start, target.end };
    IEnumerable<ACG.MapPoint> points = originalPoints.Select(x => _pointConverter.Convert(x));
    return new ACG.PolylineBuilderEx(
      points,
      ACG.AttributeFlags.HasZ,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
