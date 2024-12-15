using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.HostApp.Extensions;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.ArcGis.Operations.Send;

/// <summary>
/// Stateless builder object to turn an ISendFilter into a <see cref="Base"/> object
/// </summary>
public class ArcGISRootObjectBuilder : IRootObjectBuilder<ADM.MapMember>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ArcGISLayerUnpacker _layerUnpacker;
  private readonly ArcGISColorUnpacker _colorUnpacker;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _converterSettings;
  private readonly ILogger<ArcGISRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly MapMembersUtils _mapMemberUtils;

  public ArcGISRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    ArcGISLayerUnpacker layerUnpacker,
    ArcGISColorUnpacker colorUnpacker,
    IConverterSettingsStore<ArcGISConversionSettings> converterSettings,
    IRootToSpeckleConverter rootToSpeckleConverter,
    ILogger<ArcGISRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    MapMembersUtils mapMemberUtils
  )
  {
    _sendConversionCache = sendConversionCache;
    _layerUnpacker = layerUnpacker;
    _colorUnpacker = colorUnpacker;
    _converterSettings = converterSettings;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _mapMemberUtils = mapMemberUtils;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ADM.MapMember> layers,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // TODO: add a warning if Geographic CRS is set
    // "Data has been sent in the units 'degrees'. It is advisable to set the project CRS to Projected type (e.g. EPSG:32631) to be able to receive geometry correctly in CAD/BIM software"


    // 0 - Create Root collection and attach CRS properties
    // CRS properties are useful for data based workflows coming out of gis applications
    SpatialReference sr = _converterSettings.Current.ActiveCRSoffsetRotation.SpatialReference;
    Dictionary<string, object?> spatialReference =
      new()
      {
        ["name"] = sr.Name,
        ["unit"] = sr.Unit.Name,
        ["wkid"] = sr.Wkid,
        ["wkt"] = sr.Wkt,
      };

    Dictionary<string, object?> crs =
      new()
      {
        ["trueNorthRadians"] = _converterSettings.Current.ActiveCRSoffsetRotation.TrueNorthRadians,
        ["latOffset"] = _converterSettings.Current.ActiveCRSoffsetRotation.LatOffset,
        ["lonOffset"] = _converterSettings.Current.ActiveCRSoffsetRotation.LonOffset,
        ["spatialReference"] = spatialReference
      };

    Collection rootCollection =
      new()
      {
        name = ADM.MapView.Active.Map.Name,
        ["units"] = _converterSettings.Current.SpeckleUnits,
        ["crs"] = crs
      };

    // 1 - Unpack the selected mapmembers
    // In Arcgis, mapmembers are collections of other mapmember or objects.
    // We need to unpack the selected mapmembers into all leaf-level mapmembers (containing just objects) and build the root collection structure during unpacking.
    // Mapmember dynamically attached properties are also added at this step.
    List<ADM.MapMember> unpackedLayers;
    ADM.Map map = ADM.MapView.Active.Map;
    List<ADM.MapMember> layersOrdered = _mapMemberUtils.GetMapMembersInOrder(map, layers);
    using (var _ = _activityFactory.Start("Unpacking selection"))
    {
      unpackedLayers = await QueuedTask
        .Run(() => _layerUnpacker.UnpackSelectionAsync(layersOrdered, rootCollection))
        .ConfigureAwait(false);
    }

    List<SendConversionResult> results = new(unpackedLayers.Count);
    onOperationProgressed.Report(new("Converting", null));
    using (var convertingActivity = _activityFactory.Start("Converting objects"))
    {
      int count = 0;
      foreach (ADM.MapMember layer in unpackedLayers)
      {
        ct.ThrowIfCancellationRequested();
        string layerApplicationId = layer.GetSpeckleApplicationId();

        try
        {
          // get the corresponding collection for this layer - we'll add all converted objects to the collection
          if (_layerUnpacker.CollectionCache.TryGetValue(layerApplicationId, out Collection? layerCollection))
          {
            var status = Status.SUCCESS;
            var sdkStatus = SdkActivityStatusCode.Ok;

            // TODO: check cache first to see if this layer was previously converted
            /*
            if (_sendConversionCache.TryGetValue(
              sendInfo.ProjectId,
              layerApplicationId,
              out ObjectReference? value
            ))
            {

            }
            */

            switch (layer)
            {
              case ADM.FeatureLayer featureLayer:
                List<Base> convertedFeatureLayerObjects = await QueuedTask
                  .Run(() => ConvertFeatureLayerObjectsAsync(featureLayer))
                  .ConfigureAwait(false);
                layerCollection.elements.AddRange(convertedFeatureLayerObjects);
                break;
              case ADM.RasterLayer rasterLayer:
                List<Base> convertedRasterLayerObjects = await QueuedTask
                  .Run(() => ConvertRasterLayerObjectsAsync(rasterLayer))
                  .ConfigureAwait(false);
                layerCollection.elements.AddRange(convertedRasterLayerObjects);
                break;
              case ADM.LasDatasetLayer lasDatasetLayer:
                List<Base> convertedLasDatasetObjects = await QueuedTask
                  .Run(() => ConvertLasDatasetLayerObjectsAsync(lasDatasetLayer))
                  .ConfigureAwait(false);
                layerCollection.elements.AddRange(convertedLasDatasetObjects);
                break;
              default:
                status = Status.ERROR;
                sdkStatus = SdkActivityStatusCode.Error;
                break;
            }
            results.Add(new(status, layerApplicationId, layer.GetType().Name, layerCollection));
            convertingActivity?.SetStatus(sdkStatus);
          }
          else
          {
            throw new SpeckleException($"No converted Collection found for layer {layerApplicationId}.");
          }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogSendConversionError(ex, layer.GetType().Name);
          results.Add(new(Status.ERROR, layerApplicationId, layer.GetType().Name, null, ex));
          convertingActivity?.SetStatus(SdkActivityStatusCode.Error);
          convertingActivity?.RecordException(ex);
        }

        onOperationProgressed.Report(new("Converting", (double)++count / layersOrdered.Count));
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
    }

    // 3 -  Add Color Proxies
    rootCollection[ProxyKeys.COLOR] = _colorUnpacker.ColorProxyCache.Values.ToList();

    return new RootObjectBuilderResult(rootCollection, results);
  }

  private async Task<List<Base>> ConvertFeatureLayerObjectsAsync(ADM.FeatureLayer featureLayer)
  {
    List<Base> convertedObjects = new();
    await QueuedTask
      .Run(() =>
      {
        // store the layer renderer for color unpacking
        _colorUnpacker.StoreRendererAndFields(featureLayer);

        // search the rows of the layer, where each row is treated like an object
        // RowCursor is IDisposable but is not being correctly picked up by IDE warnings.
        // This means we need to be carefully adding using statements based on the API documentation coming from each method/class
        using (ACD.RowCursor rowCursor = featureLayer.Search())
        {
          while (rowCursor.MoveNext())
          {
            // Same IDisposable issue appears to happen on Row class too. Docs say it should always be disposed of manually by the caller.
            using (ACD.Row row = rowCursor.Current)
            {
              Base converted = _rootToSpeckleConverter.Convert(row);
              convertedObjects.Add(converted);

              // process the object color
              _colorUnpacker.ProcessFeatureLayerColor(row);
            }
          }
        }
      })
      .ConfigureAwait(false);

    return convertedObjects;
  }

  // POC: raster colors are stored as mesh vertex colors in RasterToSpeckleConverter. Should probably move to color unpacker.
  private async Task<List<Base>> ConvertRasterLayerObjectsAsync(ADM.RasterLayer rasterLayer)
  {
    List<Base> convertedObjects = new();
    await QueuedTask
      .Run(() =>
      {
        Base converted = _rootToSpeckleConverter.Convert(rasterLayer.GetRaster());
        convertedObjects.Add(converted);
      })
      .ConfigureAwait(false);

    return convertedObjects;
  }

  private async Task<List<Base>> ConvertLasDatasetLayerObjectsAsync(ADM.LasDatasetLayer lasDatasetLayer)
  {
    List<Base> convertedObjects = new();

    try
    {
      await QueuedTask
        .Run(() =>
        {
          // store the layer renderer for color unpacking
          _colorUnpacker.StoreRenderer(lasDatasetLayer);

          using (
            ACD.Analyst3D.LasPointCursor ptCursor = lasDatasetLayer.SearchPoints(new ACD.Analyst3D.LasPointFilter())
          )
          {
            while (ptCursor.MoveNext())
            {
              using (ACD.Analyst3D.LasPoint pt = ptCursor.Current)
              {
                Base converted = _rootToSpeckleConverter.Convert(pt);
                convertedObjects.Add(converted);

                // process the object color
                _colorUnpacker.ProcessLasLayerColor(pt);
              }
            }
          }
        })
        .ConfigureAwait(false);
    }
    catch (ACD.Exceptions.TinException ex)
    {
      throw new SpeckleException($"3D analyst extension is not enabled for .las layer operations", ex);
    }

    return convertedObjects;
  }
}
