using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Polyline), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolylineToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Polyline, ACG.Polyline>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PolylineToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public HostResult Convert(Base target) => HostResult.Success(  Convert((SOG.Polyline)target));

  public ACG.Polyline Convert(SOG.Polyline target)
  {
    List<SOG.Point> originalPts = target.GetPoints();
    var points = originalPts.Select(x => _pointConverter.Convert(x)).ToList();
    if (target.closed && originalPts[0] != originalPts[^1])
    {
      points.Add(points[0]);
    }
    return new ACG.PolylineBuilderEx(
      points,
      ACG.AttributeFlags.HasZ,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
