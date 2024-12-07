using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

/// <summary>
/// Converts a Polygon feature to a list of Mesh from the polygon boundary, and polylines for any inner loops.
/// This is a placeholder conversion since we don't have a polygon class or meshing strategy for interior loops yet.
/// </summary>
public class PolygonFeatureToSpeckleConverter : ITypedConverter<ACG.Polygon, List<Base>>
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

  public List<Base> Convert(ACG.Polygon target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic30235.html
    int partCount = target.PartCount;
    List<Base> parts = new();

    if (partCount == 0)
    {
      throw new ValidationException("ArcGIS Polygon contains no parts");
    }

    for (int idx = 0; idx < partCount; idx++)
    {
      // get the part polyline
      ACG.ReadOnlySegmentCollection segmentCollection = target.Parts[idx];
      SOG.Polyline polyline = _segmentConverter.Convert(segmentCollection);

      // create a mesh from the polyline if this is the exterior part
      if (target.IsExteriorRing(idx))
      {
        int vertexCount = polyline.value.Count / 3;
        List<int> faces = Enumerable.Range(0, vertexCount).ToList();

        SOG.Mesh mesh =
          new()
          {
            vertices = polyline.value,
            faces = faces,
            units = _settingsStore.Current.SpeckleUnits
          };

        parts.Add(mesh);
      }
      // otherwise, create polylines
      else
      {
        parts.Add(polyline);
      }
    }

    return parts;
  }
}
