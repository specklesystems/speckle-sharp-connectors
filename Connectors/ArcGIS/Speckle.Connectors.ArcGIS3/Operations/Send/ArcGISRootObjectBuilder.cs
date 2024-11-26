using System.Diagnostics;
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
            else // for actual layers, not yet converted
            {
              converted = await QueuedTask
                .Run(() => (Collection)_rootToSpeckleConverter.Convert(mapMember))
                .ConfigureAwait(false);

              _layerUnpacker.AddLayerProps(applicationId, mapMember, converted, globalUnits, activeCRS);
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
