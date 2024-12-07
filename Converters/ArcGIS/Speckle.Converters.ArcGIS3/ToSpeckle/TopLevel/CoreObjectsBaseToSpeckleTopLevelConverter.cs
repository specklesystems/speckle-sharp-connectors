using ArcGIS.Core.Data.Raster;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.ToSpeckle.Helpers;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(AC.CoreObjectsBase), 0)]
public class CoreObjectsBaseToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> _polylineConverter;
  private readonly ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> _multipatchConverter;
  private readonly ITypedConverter<Raster, SOG.Mesh> _gisRasterConverter;
  private readonly ITypedConverter<LasDatasetLayer, Speckle.Objects.Geometry.Pointcloud> _pointcloudConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public CoreObjectsBaseToSpeckleTopLevelConverter(
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter,
    DisplayValueExtractor displayValueExtractor,
    ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> polylineConverter,
    ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> multipatchConverter,
    ITypedConverter<Raster, SOG.Mesh> gisRasterConverter,
    ITypedConverter<LasDatasetLayer, Speckle.Objects.Geometry.Pointcloud> pointcloudConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _displayValueExtractor = displayValueExtractor;
    _polylineConverter = polylineConverter;
    _multipatchConverter = multipatchConverter;
    _gisRasterConverter = gisRasterConverter;
    _pointcloudConverter = pointcloudConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((AC.CoreObjectsBase)target);

  private ArcgisObject Convert(AC.CoreObjectsBase target)
  {
    string type = target.GetType().Name;

    // get display value
    List<Base> display = _displayValueExtractor.GetDisplayValue(target).ToList();

    // get properties


    ArcgisObject result =
      new()
      {
        name = type,
        type = type,
        displayValue = display,
        units = _settingsStore.Current.SpeckleUnits
      };

    return result;

    if (target is LasDatasetLayer pointcloudLayer)
    {
      Speckle.Objects.Geometry.Pointcloud cloud = _pointcloudConverter.Convert(pointcloudLayer);
      return new GisObject()
      {
        type = GISLayerGeometryType.POINTCLOUD,
        name = "Pointcloud",
        applicationId = "",
        displayValue = new List<Base>() { cloud },
      };
    }

    throw new NotImplementedException($"Conversion of object type {target.GetType()} is not supported");
  }
}
