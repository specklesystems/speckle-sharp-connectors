using System.Diagnostics;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.ArcGIS.HostApp;
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
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ArcGis.Operations.Send;

/// <summary>
/// Stateless builder object to turn an ISendFilter into a <see cref="Base"/> object
/// </summary>
public class ArcGISRootObjectBuilder : IRootObjectBuilder<ADM.MapMember>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ArcGISLayerUnpacker _layerUnpacker;
  private readonly ArcGISColorManager _colorManager;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _converterSettings;
  private readonly MapMembersUtils _mapMemberUtils;
  private readonly ILogger<ArcGISRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  public ArcGISRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    ArcGISLayerUnpacker layerUnpacker,
    ArcGISColorManager colorManager,
    IConverterSettingsStore<ArcGISConversionSettings> converterSettings,
    IRootToSpeckleConverter rootToSpeckleConverter,
    MapMembersUtils mapMemberUtils,
    ILogger<ArcGISRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory
  )
  {
    _sendConversionCache = sendConversionCache;
    _layerUnpacker = layerUnpacker;
    _colorManager = colorManager;
    _converterSettings = converterSettings;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _mapMemberUtils = mapMemberUtils;
    _logger = logger;
    _activityFactory = activityFactory;
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

    // TODO: send caching

    int count = 0;

    Collection rootCollection =
      new() { name = ADM.MapView.Active.Map.Name, ["units"] = _converterSettings.Current.SpeckleUnits };

    // 1 - Unpack the selected mapmembers
    // In Arcgis, mapmembers are collections of other mapmember or objects.
    // We need to unpack the selected mapmembers into their children objects and build the root collection structure during unpacking.
    List<ADM.MapMember> unpackedLayers;
    using (var _ = _activityFactory.Start("Unpacking selection"))
    {
      unpackedLayers = await QueuedTask
        .Run(() => _layerUnpacker.UnpackSelectionAsync(layers, rootCollection))
        .ConfigureAwait(false);
    }

    List<SendConversionResult> results = new(unpackedLayers.Count);
    var cacheHitCount = 0;

    onOperationProgressed.Report(new("Converting", null));
    using (var convertingActivity = _activityFactory.Start("Converting objects"))
    {
      foreach (ADM.MapMember layer in unpackedLayers)
      {
        ct.ThrowIfCancellationRequested();

        try
        {
          // get the corresponding collection for this layer - we'll add all converted objects to the collection
          if (_layerUnpacker.CollectionCache.TryGetValue(layer.URI, out Collection? layerCollection))
          {
            switch (layer)
            {
              case ADM.FeatureLayer featureLayer:
                List<Base> convertedFeatureLayerObjects = await QueuedTask
          .Run(() => ConvertFeatureLayerObjectsAsync(featureLayer, layerCollection))
          .ConfigureAwait(false);
                layerCollection.elements.AddRange(convertedFeatureLayerObjects);
                break;
              case ADM.RasterLayer rasterLayer:
                List<Base> convertedRasterLayerObjects = await QueuedTask.Run(() => ConvertRasterLayerObjectsAsync(rasterLayer)).ConfigureAwait(false);
                layerCollection.elements.AddRange(convertedRasterLayerObjects);
                break;
              case ADM.LasDatasetLayer lasDatasetLayer:
                List<Base> convertedLasDatasetObjects = await QueuedTask.Run(() => ConvertLasDatasetLayerObjectsAsync(lasDatasetLayer)).ConfigureAwait(false);
                layerCollection.elements.AddRange(convertedLasDatasetObjects);
                break;
              default:
                // TODO: report unsupported layer type here
            }
          }
          else
          {
            // TODO: throw error, a collection should have been converted for this layer in the layerUnpacker.
          }
          results.Add(new(Status.SUCCESS, layer.URI, sourceType, layerCollection));
          convertingActivity?.SetStatus(SdkActivityStatusCode.Ok);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogSendConversionError(ex, sourceType);
          results.Add(new(Status.ERROR, layer.URI, sourceType, null, ex));
          convertingActivity?.SetStatus(SdkActivityStatusCode.Error);
          convertingActivity?.RecordException(ex);
        }

        onOperationProgressed.Report(new("Converting", (double)++count / layers.Count));
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
    }

    // POC: Add Color Proxies
    List<ColorProxy> colorProxies = _colorManager.UnpackColors(layersWithDisplayPriority);
    rootCollection[ProxyKeys.COLOR] = colorProxies;

    // POC: Log would be nice, or can be removed.
    Debug.WriteLine(
      $"Cache hit count {cacheHitCount} out of {layers.Count} ({(double)cacheHitCount / objlayersects.Count})"
    );

    return new RootObjectBuilderResult(rootCollection, results);
  }

  private async Task<List<Base>> ConvertFeatureLayerObjectsAsync(ADM.FeatureLayer featureLayer, Collection featureLayerCollection)
  {
      if (featureLayerCollection["fields"] is Dictionary<string, string> visibleFields)
      {
        List<Base> convertedObjects = new();

        await QueuedTask
      .Run(() =>
      {
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
              Base converted = _rootToSpeckleConverter.Convert((row, visibleFields));
              convertedObjects.Add(converted);
            }
          }
        }
      })
      .ConfigureAwait(false);

        return convertedObjects;
      }
      else
      {
        // TODO: throw exception here, this layer should have fields
      }
  }

  private async Task<List<Base>> ConvertRasterLayerObjectsAsync(ADM.RasterLayer rasterLayer)
  {
    List<Base> convertedObjects = new();
    await QueuedTask
      .Run(() =>
      {
        Base converted = _rootToSpeckleConverter.Convert((rasterLayer.GetRaster(), new Dictionary<string,string>()));
        convertedObjects.Add(converted);

      })
      .ConfigureAwait(false);

    return convertedObjects;
  }

  private async Task<List<Base>> ConvertLasDatasetLayerObjectsAsync(ADM.LasDatasetLayer lasDatasetLayer)
  {
    List<Base> convertedObjects = new();
    await QueuedTask
     .Run(() =>
     {
       using (ACD.Analyst3D.LasPointCursor ptCursor = lasDatasetLayer.SearchPoints(new ACD.Analyst3D.LasPointFilter()))
       {
         while (ptCursor.MoveNext())
         {
           using (ACD.Analyst3D.LasPoint pt = ptCursor.Current)
           {
             Base converted = _rootToSpeckleConverter.Convert((pt, new Dictionary<string, string>()));
             convertedObjects.Add(converted);
           }
         }
       }
     })
      .ConfigureAwait(false);

    return convertedObjects;
  }



}

