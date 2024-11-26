using System.Diagnostics;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Objects.GIS;
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
  private readonly ITypedConverter<(Row, string), GisObject> _gisObjectConverter;
  private readonly ITypedConverter<(Row, IReadOnlyCollection<string>), Base> _attributeConverter;
  private readonly ITypedConverter<Raster, RasterElement> _gisRasterConverter;
  private readonly ITypedConverter<LasDatasetLayer, Speckle.Objects.Geometry.Pointcloud> _pointcloudConverter;

  public ArcGISRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    ArcGISLayerUnpacker layerUnpacker,
    ArcGISColorManager colorManager,
    IConverterSettingsStore<ArcGISConversionSettings> converterSettings,
    IRootToSpeckleConverter rootToSpeckleConverter,
    MapMembersUtils mapMemberUtils,
    ILogger<ArcGISRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ITypedConverter<(Row, string), GisObject> gisObjectConverter,
    ITypedConverter<(Row, IReadOnlyCollection<string>), Base> attributeConverter,
    ITypedConverter<Raster, RasterElement> gisRasterConverter,
    ITypedConverter<LasDatasetLayer, Speckle.Objects.Geometry.Pointcloud> pointcloudConverter
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
    _gisRasterConverter = gisRasterConverter;
    _pointcloudConverter = pointcloudConverter;
  }

#pragma warning disable CA1506
  public async Task<RootObjectBuilderResult> Build(
#pragma warning restore CA1506
    IReadOnlyList<MapMember> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // TODO: add a warning if Geographic CRS is set
    // "Data has been sent in the units 'degrees'. It is advisable to set the project CRS to Projected type (e.g. EPSG:32631) to be able to receive geometry correctly in CAD/BIM software"

    int count = 0;

    Collection rootObjectCollection = new() { name = MapView.Active.Map.Name }; //TODO: Collections
    string globalUnits = _converterSettings.Current.SpeckleUnits;
    CRSoffsetRotation activeCRS = _converterSettings.Current.ActiveCRSoffsetRotation;

    rootObjectCollection["units"] = globalUnits;

    List<SendConversionResult> results = new(objects.Count);
    var cacheHitCount = 0;
    List<(ILayerContainer, Collection)> nestedGroups = new();

    // reorder selected layers by Table of Content (TOC) order
    List<(MapMember, int)> layersWithDisplayPriority = _mapMemberUtils.GetLayerDisplayPriority(
      MapView.Active.Map,
      objects
    );

    onOperationProgressed.Report(new("Converting", null));
    using (var __ = _activityFactory.Start("Converting objects"))
    {
      foreach ((MapMember mapMember, _) in layersWithDisplayPriority)
      {
        ct.ThrowIfCancellationRequested();

        using (var convertingActivity = _activityFactory.Start("Converting object"))
        {
          string applicationId = mapMember.URI;
          string sourceType = mapMember.GetType().Name;

          try
          {
            Base converted;
            _layerUnpacker.ResetNestedGroups(applicationId, nestedGroups);

            // check if the converted layer is cached
            bool cached = _sendConversionCache.TryGetValue(
              sendInfo.ProjectId,
              applicationId,
              out ObjectReference? value
            );

            if (mapMember is ILayerContainer layerContainer) // for group layers
            {
              converted = _layerUnpacker.InsertNestedGroup(layerContainer, applicationId, nestedGroups);
            }
            else if (cached && value is not null) // for actual layers which are cached
            {
              converted = value;
              cacheHitCount++; // is it actually used?
            }
            else // for actual layers, TO CONVERT
            {
              converted = await QueuedTask
                .Run(() => _layerUnpacker.AddLayerWithProps(applicationId, mapMember, globalUnits, activeCRS))
                .ConfigureAwait(false);
              // 'converted' is now VectorLayer or RasterLayer Collection

              if (mapMember is FeatureLayer featureLayer && converted is VectorLayer convertedVector)
              {
                IReadOnlyCollection<string> visibleAttributes = convertedVector.attributes.DynamicPropertyKeys;
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
                          GisObject element = _gisObjectConverter.Convert((row, appId));
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
                await QueuedTask
                  .Run(() =>
                  {
                    RasterElement element = _gisRasterConverter.Convert(arcGisRasterLayer.GetRaster());
                    element.applicationId = $"{arcGisRasterLayer.URI}_0";
                    convertedRasterLayer.elements.Add(element);
                  })
                  .ConfigureAwait(false);

                converted = convertedRasterLayer;
              }
              else if (mapMember is LasDatasetLayer pointcloudLayer && converted is Collection convertedPointcloudLayer)
              {
                Speckle.Objects.Geometry.Pointcloud cloud = await QueuedTask
                  .Run(() => _pointcloudConverter.Convert(pointcloudLayer))
                  .ConfigureAwait(false);
                convertedPointcloudLayer.elements.Add(cloud);

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

        onOperationProgressed.Report(new("Converting", (double)++count / objects.Count));
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
      $"Cache hit count {cacheHitCount} out of {objects.Count} ({(double)cacheHitCount / objects.Count})"
    );

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }
}
