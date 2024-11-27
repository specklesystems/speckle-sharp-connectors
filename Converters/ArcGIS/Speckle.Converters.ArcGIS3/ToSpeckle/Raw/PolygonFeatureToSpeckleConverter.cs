using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class PolygonFeatureToSpeckleConverter : ITypedConverter<ACG.Polygon, IReadOnlyList<SOG.Polygon>>
{
  private readonly ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> _segmentConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PolygonFeatureToSpeckleConverter(
    ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> segmentConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _segmentConverter = segmentConverter;
    _settingsStore = settingsStore;
  }

  public IReadOnlyList<SOG.Polygon> Convert(ACG.Polygon target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic30235.html
    List<SOG.Polygon> polygonList = new();
    int partCount = target.PartCount;

    if (partCount == 0)
    {
      throw new ValidationException("ArcGIS Polygon contains no parts");
    }

    SOG.Polygon? polygon = null;

    // test each part for "exterior ring"
    for (int idx = 0; idx < partCount; idx++)
    {
      ACG.ReadOnlySegmentCollection segmentCollection = target.Parts[idx];
      SOG.Polyline polyline = _segmentConverter.Convert(segmentCollection);

      bool isExteriorRing = target.IsExteriorRing(idx);
      if (isExteriorRing)
      {
        polygon = new()
        {
          boundary = polyline,
          voids = new List<ICurve>(),
          units = _settingsStore.Current.SpeckleUnits
        };
        polygonList.Add(polygon);
      }
      else // interior part
      {
        if (polygon == null)
        {
          throw new ValidationException("Invalid ArcGIS Polygon. Interior part preceeding the exterior ring.");
        }
        polygon.voids.Add(polyline);
      }
    }

    return polygonList;
  }
}
