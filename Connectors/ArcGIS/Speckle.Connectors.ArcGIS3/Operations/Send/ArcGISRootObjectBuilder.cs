using System.Diagnostics;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Extensions;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.Common;
using Speckle.Objects.GIS;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ArcGis.Operations.Send;

/// <summary>
/// Stateless builder object to turn an ISendFilter into a <see cref="Base"/> object
/// </summary>
public class ArcGISRootObjectBuilder : IRootObjectBuilder<MapMember>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ArcGISColorManager _colorManager;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;
  private readonly MapMembersUtils _mapMemberUtils;
  private readonly ILogger<ArcGISRootObjectBuilder> _logger;

  public ArcGISRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    ArcGISColorManager colorManager,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore,
    IRootToSpeckleConverter rootToSpeckleConverter,
    MapMembersUtils mapMemberUtils,
    ILogger<ArcGISRootObjectBuilder> logger
  )
  {
    _sendConversionCache = sendConversionCache;
    _colorManager = colorManager;
    _settingsStore = settingsStore;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _mapMemberUtils = mapMemberUtils;
    _logger = logger;
  }

#pragma warning disable CA1506
  public async Task<RootObjectBuilderResult> Build(
#pragma warning restore CA1506
    IReadOnlyList<MapMember> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    // TODO: add a warning if Geographic CRS is set
    // "Data has been sent in the units 'degrees'. It is advisable to set the project CRS to Projected type (e.g. EPSG:32631) to be able to receive geometry correctly in CAD/BIM software"

    int count = 0;

    Collection rootObjectCollection = new() { name = MapView.Active.Map.Name }; //TODO: Collections

    List<SendConversionResult> results = new(objects.Count);
    var cacheHitCount = 0;
    List<(GroupLayer, Collection)> nestedGroups = new();

    // reorder selected layers by Table of Content (TOC) order
    List<(MapMember, int)> layersWithDisplayPriority = _mapMemberUtils.GetLayerDisplayPriority(
      MapView.Active.Map,
      objects
    );

    onOperationProgressed?.Invoke("Converting", null);
    using (var __ = SpeckleActivityFactory.Start("Converting objects"))
    {
      foreach ((MapMember mapMember, _) in layersWithDisplayPriority)
      {
        ct.ThrowIfCancellationRequested();

        using (var convertingActivity = SpeckleActivityFactory.Start("Converting object"))
        {
          var collectionHost = rootObjectCollection;
          string applicationId = mapMember.URI;
          string sourceType = mapMember.GetType().Name;

          Base converted;
          try
          {
            int groupCount = nestedGroups.Count; // bake here, because count will change in the loop
            // if the layer is not a part of the group, reset groups
            for (int i = 0; i < groupCount; i++)
            {
              if (nestedGroups.Count > 0 && !nestedGroups[0].Item1.Layers.Select(x => x.URI).Contains(applicationId))
              {
                nestedGroups.RemoveAt(0);
              }
              else
              {
                // break at the first group, which contains current layer
                break;
              }
            }

            // don't use cache for group layers
            if (
              mapMember is not GroupLayer
              && _sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference? value)
            )
            {
              converted = value;
              cacheHitCount++;
            }
            else
            {
              if (mapMember is GroupLayer group)
              {
                // group layer will always come before it's contained layers
                // keep active group last in the list
                converted = new Collection();
                nestedGroups.Insert(0, (group, (Collection)converted));
              }
              else
              {
                converted = await QueuedTask
                  .Run(() => (Collection)_rootToSpeckleConverter.Convert(mapMember))
                  .ConfigureAwait(false);

                // get units & Active CRS (for writing geometry coords)
                converted["units"] = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpeckleUnitString;

                var spatialRef = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference;
                converted["crs"] = new CRS
                {
                  wkt = spatialRef.Wkt,
                  name = spatialRef.Name,
                  offset_y = Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.LatOffset),
                  offset_x = Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.LonOffset),
                  rotation = Convert.ToSingle(_contextStack.Current.Document.ActiveCRSoffsetRotation.TrueNorthRadians),
                  units_native = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpeckleUnitString,
                };
              }

              // other common properties for layers and groups
              converted["name"] = mapMember.Name;
              converted.applicationId = applicationId;
            }

            if (
              nestedGroups.Count == 0
              || nestedGroups.Count == 1 && nestedGroups[0].Item2.applicationId == applicationId
            )
            {
              // add to host if no groups, or current root group
              collectionHost.elements.Add(converted);
            }
            else
            {
              // if we are adding a layer inside the group
              var parentCollection = nestedGroups.FirstOrDefault(x =>
                x.Item1.Layers.Select(y => y.URI).Contains(applicationId)
              );
              parentCollection.Item2.elements.Add(converted);
            }

            results.Add(new(Status.SUCCESS, applicationId, sourceType, converted));
            convertingActivity?.SetStatus(SpeckleActivityStatusCode.Ok);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            _logger.LogSendConversionError(ex, sourceType);
            results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
            convertingActivity?.SetStatus(SpeckleActivityStatusCode.Error);
            convertingActivity?.RecordException(ex);
          }
        }

        onOperationProgressed?.Invoke("Converting", (double)++count / objects.Count);
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleConversionException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
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
