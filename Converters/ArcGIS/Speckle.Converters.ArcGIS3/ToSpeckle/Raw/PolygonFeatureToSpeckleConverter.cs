using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

/// <summary>
/// Converts a Polygon feature to a list of polylines from the polygon boundary and inner loops.
/// This is a placeholder conversion since we don't have a polygon class or meshing strategy for interior loops yet.
/// </summary>
public class PolygonFeatureToSpeckleConverter : ITypedConverter<ACG.Polygon, IReadOnlyList<SOG.Region>>
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

  public IReadOnlyList<SOG.Region> Convert(ACG.Polygon target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic30235.html
    int partCount = target.PartCount;
    if (partCount == 0)
    {
      throw new ValidationException("ArcGIS Polygon contains no parts");
    }

    // declare Region elements
    List<SOG.Region> regions = new();
    ICurve? boundary = null;
    List<ICurve> innerLoops = new();

    // iterate through polugon parts: can be inner or outer curves,
    // can be multiple outer curves too (if multipolygon).
    for (int i = 0; i < partCount; i++)
    {
      // get the part polyline
      ACG.ReadOnlySegmentCollection segmentCollection = target.Parts[i];
      SOG.Polyline polyline = _segmentConverter.Convert(segmentCollection);

      if (!target.IsExteriorRing(i))
      {
        innerLoops.Add(polyline);
      }
      else
      {
        // save previous region (if exists)
        if (boundary is not null)
        {
          regions.Add(CreateRegion(boundary, innerLoops));
        }
        // reset values to start a new region
        boundary = polyline;
        innerLoops = [];
      }
    }
    // after all loops, create and add the last region to the list
    if (boundary is not null)
    {
      regions.Add(CreateRegion(boundary, innerLoops));
    }

    return regions;
  }

  private SOG.Region CreateRegion(ICurve boundary, List<ICurve> innerLoops)
  {
    SOG.Region newRegion =
      new()
      {
        boundary = boundary,
        innerLoops = innerLoops,
        hasHatchPattern = false,
        displayValue = [],
        units = _settingsStore.Current.SpeckleUnits
      };
    return newRegion;
  }
}
