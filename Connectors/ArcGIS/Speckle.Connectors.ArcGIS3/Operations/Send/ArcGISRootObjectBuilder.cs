using System;
using System.Diagnostics;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;
using RasterLayer = ArcGIS.Desktop.Mapping.RasterLayer;

namespace Speckle.Connectors.ArcGis.Operations.Send;

/// <summary>
/// Stateless builder object to turn an ISendFilter into a <see cref="Base"/> object
/// </summary>
public class ArcGISRootObjectBuilder : IRootObjectBuilder<MapMember>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ArcGISLayerUnpacker _layerUnpacker;
  private readonly ArcGISColorManager _colorManager;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _converterSettings;
  private readonly MapMembersUtils _mapMemberUtils;
  private readonly ILogger<ArcGISRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ITypedConverter<object, GisObject> _gisObjectConverter;
  private readonly ITypedConverter<(Row, IReadOnlyCollection<string>), Base> _attributeConverter;

  public ArcGISRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    ArcGISLayerUnpacker layerUnpacker,
    ArcGISColorManager colorManager,
    IConverterSettingsStore<ArcGISConversionSettings> converterSettings,
    IRootToSpeckleConverter rootToSpeckleConverter,
    MapMembersUtils mapMemberUtils,
    ILogger<ArcGISRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ITypedConverter<object, GisObject> gisObjectConverter,
    ITypedConverter<(Row, IReadOnlyCollection<string>), Base> attributeConverter
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
    _gisObjectConverter = gisObjectConverter;
    _attributeConverter = attributeConverter;
  }

#pragma warning disable CA1506
  public async Task<RootObjectBuilderResult> Build(
#pragma warning restore CA1506
    IReadOnlyList<MapMember> layers,
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
      new() { name = MapView.Active.Map.Name, ["units"] = _converterSettings.Current.SpeckleUnits };

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
    using (var __ = _activityFactory.Start("Converting objects"))
    {
      foreach (ADM.MapMember layer in unpackedLayers)
      {
        ct.ThrowIfCancellationRequested();

        switch (layer)
        {
          case ADM.FeatureLayer featureLayer:
            ConvertFeatureLayerObjects(featureLayer);
            break;
          case ADM.RasterLayer rasterLayer:
            break;
          case ADM.LasDatasetLayer pointcloudLayer:
            break;
          default:
            // TODO: report unsupported layer type here
        }

        using (var convertingActivity = _activityFactory.Start("Converting object"))
        {
          try
          {
            Base converted;

            else // for actual layers, TO CONVERT
            {
              if (
                mapMember is FeatureLayer featureLayer
                && converted is GisLayer convertedVector
                && convertedVector["attributes"] is Base attributes
              )
              {
                IReadOnlyCollection<string> visibleAttributes = attributes.DynamicPropertyKeys;
                await QueuedTask
                  .Run(() =>
                  {
                    // search the rows of the layer, where each row = GisFeature
                    // RowCursor is IDisposable but is not being correctly picked up by IDE warnings.
                    // This means we need to be carefully adding using statements based on the API documentation coming from each method/class
                    int count = 1;
                    using (RowCursor rowCursor = featureLayer.Search())
                    {
                      while (rowCursor.MoveNext())
                      {
                        // Same IDisposable issue appears to happen on Row class too. Docs say it should always be disposed of manually by the caller.
                        string appId = $"{featureLayer.URI}_{count}";
                        using (Row row = rowCursor.Current)
                        {
                          GisObject elementNoId = _gisObjectConverter.Convert(row);
                          GisObject element =
                            new()
                            {
                              type = elementNoId.type,
                              name = elementNoId.name,
                              applicationId = appId,
                              displayValue = elementNoId.displayValue,
                            };
                          element["properties"] = _attributeConverter.Convert((row, visibleAttributes));

                          // add converted feature to converted layer
                          convertedVector.elements.Add(element);
                        }

                        count++;
                      }
                    }
                  })
                  .ConfigureAwait(false);

                converted = convertedVector;
              }
              else if (mapMember is RasterLayer arcGisRasterLayer && converted is Collection convertedRasterLayer)
              {
                string appId = $"{arcGisRasterLayer.URI}_0";
                await QueuedTask
                  .Run(() =>
                  {
                    GisObject elementNoId = _gisObjectConverter.Convert(arcGisRasterLayer.GetRaster());
                    GisObject element =
                      new()
                      {
                        type = elementNoId.type,
                        name = elementNoId.name,
                        applicationId = appId,
                        displayValue = elementNoId.displayValue,
                      };
                    convertedRasterLayer.elements.Add(element);
                  })
                  .ConfigureAwait(false);

                converted = convertedRasterLayer;
              }
              else if (mapMember is LasDatasetLayer pointcloudLayer && converted is Collection convertedPointcloudLayer)
              {
                string appId = $"{pointcloudLayer.URI}_0";
                GisObject elementNoId = _gisObjectConverter.Convert(pointcloudLayer);
                GisObject element =
                  new()
                  {
                    type = elementNoId.type,
                    name = elementNoId.name,
                    applicationId = appId,
                    displayValue = elementNoId.displayValue,
                  };
                convertedPointcloudLayer.elements.Add(element);

                converted = convertedPointcloudLayer;
              }
            }

            _layerUnpacker.AddConvertedToRoot(applicationId, converted, rootObjectCollection, nestedGroups);

            results.Add(new(Status.SUCCESS, applicationId, sourceType, converted));
            convertingActivity?.SetStatus(SdkActivityStatusCode.Ok);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            _logger.LogSendConversionError(ex, sourceType);
            results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
            convertingActivity?.SetStatus(SdkActivityStatusCode.Error);
            convertingActivity?.RecordException(ex);
          }
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
    rootObjectCollection[ProxyKeys.COLOR] = colorProxies;

    // POC: Log would be nice, or can be removed.
    Debug.WriteLine(
      $"Cache hit count {cacheHitCount} out of {layers.Count} ({(double)cacheHitCount / objlayersects.Count})"
    );

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private async void ConvertFeatureLayerObjects(ADM.FeatureLayer featureLayer)
  {
    // get the corresponding collection for this layer - we'll add all converted objects to the collection
    if (_layerUnpacker.CollectionCache.TryGetValue(featureLayer.URI, out Collection? featureLayerCollection))
    {
      var visibleAttributes = featureLayerCollection["fields"] as Dictionary<string, string>;
      
    }
    else
    {
      // TODO: throw exception here, something went wrong with layer conversion
    }

    await QueuedTask
      .Run(() =>
      {
        // search the rows of the layer, where each row is treated like an object
        // RowCursor is IDisposable but is not being correctly picked up by IDE warnings.
        // This means we need to be carefully adding using statements based on the API documentation coming from each method/class
        int count = 1;
        using (RowCursor rowCursor = featureLayer.Search())
        {
          while (rowCursor.MoveNext())
          {
            // Same IDisposable issue appears to happen on Row class too. Docs say it should always be disposed of manually by the caller.
            string appId = $"{featureLayer.URI}_{count}";
            using (Row row = rowCursor.Current)
            {

              GisObject elementNoId = _gisObjectConverter.Convert(row);
              GisObject element =
                new()
                {
                  type = elementNoId.type,
                  name = elementNoId.name,
                  applicationId = appId,
                  displayValue = elementNoId.displayValue,
                };
              element["properties"] = _attributeConverter.Convert((row, visibleAttributes));

              // add converted feature to converted layer
              convertedVector.elements.Add(element);
            }

            count++;
          }
        }
      })
      .ConfigureAwait(false);
  }


}

