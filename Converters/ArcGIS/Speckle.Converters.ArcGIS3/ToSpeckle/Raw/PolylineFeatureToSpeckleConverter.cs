using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class PolyineFeatureToSpeckleConverter : ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>>
{
  private readonly ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> _segmentConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PolyineFeatureToSpeckleConverter(
    ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> segmentConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _segmentConverter = segmentConverter;
    _settingsStore = settingsStore;
  }

  public IReadOnlyList<SOG.Polyline> Convert(ACG.Polyline target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic8480.html
    List<SOG.Polyline> polylineList = new();
    ACG.Polyline polylineToConvert = target;

    // densify the polylines with curves using precision value of the Map's Spatial Reference
    if (target.HasCurves)
    {
      double tolerance = _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference.XYTolerance;
      double conversionFactorToMeter = _settingsStore
        .Current
        .ActiveCRSoffsetRotation
        .SpatialReference
        .Unit
        .ConversionFactor;

      var densifiedPolyline = ACG.GeometryEngine.Instance.DensifyByDeviation(
        target,
        tolerance * conversionFactorToMeter
      );
      if (densifiedPolyline == null)
      {
        throw new ArgumentException("Polyline densification failed");
      }
      polylineToConvert = (ACG.Polyline)densifiedPolyline;
    }

    foreach (var segmentCollection in polylineToConvert.Parts)
    {
      polylineList.Add(_segmentConverter.Convert(segmentCollection));
    }
    return polylineList;
  }
}
