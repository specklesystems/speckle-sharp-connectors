using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.HostApp.Extensions;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Common.Builders;
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
  private readonly ArcGISLayerUnpacker _layerUnpacker;
  private readonly ArcGISColorUnpacker _colorUnpacker;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _converterSettings;
  private readonly ILogger<ArcGISRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly MapMembersUtils _mapMemberUtils;

  public ArcGISRootObjectBuilder(
    ArcGISLayerUnpacker layerUnpacker,
    ArcGISColorUnpacker colorUnpacker,
    IConverterSettingsStore<ArcGISConversionSettings> converterSettings,
    IRootToSpeckleConverter rootToSpeckleConverter,
    ILogger<ArcGISRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    MapMembersUtils mapMemberUtils
  )
  {
    _layerUnpacker = layerUnpacker;
    _colorUnpacker = colorUnpacker;
    _converterSettings = converterSettings;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _mapMemberUtils = mapMemberUtils;
  }

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ADM.MapMember> layers,
    SendInfo __,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  ) => QueuedTask.Run(() => BuildInternal(layers, __, onOperationProgressed, cancellationToken));

  private async Task<RootObjectBuilderResult> BuildInternal(
    IReadOnlyList<ADM.MapMember> layers,
    SendInfo __,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
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
    IEnumerable<ADM.MapMember> layersOrdered = _mapMemberUtils.GetMapMembersInOrder(map, layers);
    using (var _ = _activityFactory.Start("Unpacking selection"))
    {
      unpackedLayers = _layerUnpacker.UnpackSelection(layersOrdered, rootCollection);
    }

    List<SendConversionResult> results = new(unpackedLayers.Count);
    onOperationProgressed.Report(new("Converting", null));
    using (var convertingActivity = _activityFactory.Start("Converting objects"))
    {
      long count = 0;
      // count number of features to convert. Raster layers are counter as 1 feature for now (not ideal)
      Dictionary<ADM.MapMember, long> layersWithFeatureCount = CountAllFeaturesInLayers(unpackedLayers);
      long allFeaturesCount = layersWithFeatureCount.Values.Sum();

      foreach (var (layer, layerFeatureCount) in layersWithFeatureCount)
      {
        cancellationToken.ThrowIfCancellationRequested();
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
                List<Base> convertedFeatureLayerObjects = ConvertFeatureLayerObjects(
                  featureLayer,
                  count,
                  allFeaturesCount,
                  onOperationProgressed,
                  cancellationToken
                );
                layerCollection.elements.AddRange(convertedFeatureLayerObjects);
                break;
              case ADM.RasterLayer rasterLayer:
                // Don't pass count and cancellation token to layer conversion here, because Raster layer is counted as 1 object for now
                List<Base> convertedRasterLayerObjects = ConvertRasterLayerObjects(
                  rasterLayer,
                  count,
                  allFeaturesCount,
                  onOperationProgressed,
                  cancellationToken
                );
                layerCollection.elements.AddRange(convertedRasterLayerObjects);
                break;
              case ADM.LasDatasetLayer lasDatasetLayer:
                List<Base> convertedLasDatasetObjects = ConvertLasDatasetLayerObjects(
                  lasDatasetLayer,
                  count,
                  allFeaturesCount,
                  onOperationProgressed,
                  cancellationToken
                );
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

        await Task.Yield();
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

  private Dictionary<ADM.MapMember, long> CountAllFeaturesInLayers(List<ADM.MapMember> unpackedLayers)
  {
    Dictionary<ADM.MapMember, long> layersFeatureCount = new();

    foreach (ADM.MapMember layer in unpackedLayers)
    {
      switch (layer)
      {
        case ADM.FeatureLayer featureLayer:
          layersFeatureCount.Add(featureLayer, featureLayer.GetFeatureClass().GetCount());
          break;
        case ADM.RasterLayer rasterLayer:
          // count Raster layer as 1 feature: not optimal but this is the approach for now
          layersFeatureCount.Add(rasterLayer, 1);
          break;
        case ADM.LasDatasetLayer lasDatasetLayer:
          var dataset = lasDatasetLayer.GetLasDataset();
          // simple dataset.GetPointCount() keeps returning null, so switched to EstimatePointCount
          layersFeatureCount.Add(
            lasDatasetLayer,
            (long)dataset.EstimatePointCount(dataset.GetDefinition().GetExtent())
          );
          break;
      }
    }
    return layersFeatureCount;
  }

  private List<Base> ConvertFeatureLayerObjects(
    ADM.FeatureLayer featureLayer,
    long count,
    long allFeaturesCount,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    string layerApplicationId = featureLayer.GetSpeckleApplicationId();
    List<Base> convertedObjects = new();
    // store the layer renderer for color unpacking
    _colorUnpacker.StoreRendererAndFields(featureLayer);

    // search the rows of the layer, where each row is treated like an object
    // RowCursor is IDisposable but is not being correctly picked up by IDE warnings.
    // This means we need to be carefully adding using statements based on the API documentation coming from each method/class
    using (ACD.RowCursor rowCursor = featureLayer.Search())
    {
      while (rowCursor.MoveNext())
      {
        // allow cancellation before every feature
        cancellationToken.ThrowIfCancellationRequested();

        // Same IDisposable issue appears to happen on Row class too. Docs say it should always be disposed of manually by the caller.
        using (ACD.Row row = rowCursor.Current)
        {
          // get application id. test for subtypes before defaulting to base type.
          Base converted = _rootToSpeckleConverter.Convert(row);
          string applicationId = row.GetSpeckleApplicationId(layerApplicationId);
          converted.applicationId = applicationId;

          convertedObjects.Add(converted);

          // process the object color
          _colorUnpacker.ProcessFeatureLayerColor(row, applicationId);
        }
        // update report
        onOperationProgressed.Report(new("Converting", (double)++count / allFeaturesCount));
      }
    }

    return convertedObjects;
  }

  // POC: raster colors are stored as mesh vertex colors in RasterToSpeckleConverter. Should probably move to color unpacker.
  private List<Base> ConvertRasterLayerObjects(
    ADM.RasterLayer rasterLayer,
    long count,
    long allFeaturesCount,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    string layerApplicationId = rasterLayer.GetSpeckleApplicationId();
    List<Base> convertedObjects = new();
    Raster raster = rasterLayer.GetRaster();

    // check cancellation token before conversion
    cancellationToken.ThrowIfCancellationRequested();
    Base converted = _rootToSpeckleConverter.Convert(raster);
    string applicationId = raster.GetSpeckleApplicationId(layerApplicationId);
    converted.applicationId = applicationId;
    convertedObjects.Add(converted);

    // update report
    onOperationProgressed.Report(new("Converting", (double)++count / allFeaturesCount));

    return convertedObjects;
  }

  private List<Base> ConvertLasDatasetLayerObjects(
    ADM.LasDatasetLayer lasDatasetLayer,
    long count,
    long allFeaturesCount,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    string layerApplicationId = lasDatasetLayer.GetSpeckleApplicationId();
    List<Base> convertedObjects = new();

    try
    {
      // store the layer renderer for color unpacking
      _colorUnpacker.StoreRenderer(lasDatasetLayer);

      using (ACD.Analyst3D.LasPointCursor ptCursor = lasDatasetLayer.SearchPoints(new ACD.Analyst3D.LasPointFilter()))
      {
        while (ptCursor.MoveNext())
        {
          // allow cancellation before every point
          cancellationToken.ThrowIfCancellationRequested();

          using (ACD.Analyst3D.LasPoint pt = ptCursor.Current)
          {
            Base converted = _rootToSpeckleConverter.Convert(pt);
            string applicationId = pt.GetSpeckleApplicationId(layerApplicationId);
            converted.applicationId = applicationId;
            convertedObjects.Add(converted);

            // process the object color
            _colorUnpacker.ProcessLasLayerColor(pt, applicationId);
          }
          // update report
          onOperationProgressed.Report(new("Converting", (double)++count / allFeaturesCount));
        }
      }
    }
    catch (ACD.Exceptions.TinException ex)
    {
      throw new SpeckleException("3D analyst extension is not enabled for .las layer operations", ex);
    }

    return convertedObjects;
  }
}
