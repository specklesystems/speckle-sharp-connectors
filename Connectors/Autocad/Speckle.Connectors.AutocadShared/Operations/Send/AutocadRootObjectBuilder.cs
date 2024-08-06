using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.Instances;
using Speckle.Core.Models.Proxies;

namespace Speckle.Connectors.Autocad.Operations.Send;

public class AutocadRootObjectBuilder : IRootObjectBuilder<AutocadRootObject>
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly string[] _documentPathSeparator = ["\\"];
  private readonly ISendConversionCache _sendConversionCache;
  private readonly AutocadInstanceObjectManager _instanceObjectsManager;
  private readonly AutocadColorManager _colorManager;
  private readonly AutocadLayerManager _layerManager;
  private readonly AutocadGroupUnpacker _groupUnpacker;
  private readonly ISyncToThread _syncToThread;

  public AutocadRootObjectBuilder(
    IRootToSpeckleConverter converter,
    ISendConversionCache sendConversionCache,
    AutocadInstanceObjectManager instanceObjectManager,
    AutocadColorManager colorManager,
    AutocadLayerManager layerManager,
    AutocadGroupUnpacker groupUnpacker,
    ISyncToThread syncToThread
  )
  {
    _converter = converter;
    _sendConversionCache = sendConversionCache;
    _instanceObjectsManager = instanceObjectManager;
    _colorManager = colorManager;
    _layerManager = layerManager;
    _groupUnpacker = groupUnpacker;
    _syncToThread = syncToThread;
  }

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<AutocadRootObject> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    return _syncToThread.RunOnThread(() =>
    {
      Collection modelWithLayers =
        new()
        {
          name = Application
            .DocumentManager.CurrentDocument.Name // POC: https://spockle.atlassian.net/browse/CNX-9319
            .Split(_documentPathSeparator, StringSplitOptions.None)
            .Reverse()
            .First()
        };

      // TODO: better handling for document and transactions!!
      Document doc = Application.DocumentManager.CurrentDocument;
      using Transaction tr = doc.Database.TransactionManager.StartTransaction();

      // Cached dictionary to create Collection for autocad entity layers. We first look if collection exists. If so use it otherwise create new one for that layer.
      List<LayerTableRecord> layers = new();
      int count = 0;

      var (atomicObjects, instanceProxies, instanceDefinitionProxies) = _instanceObjectsManager.UnpackSelection(
        objects
      );

      List<SendConversionResult> results = new();
      var cacheHitCount = 0;

      foreach (var (entity, applicationId) in atomicObjects)
      {
        ct.ThrowIfCancellationRequested();
        try
        {
          Base converted;
          if (entity is BlockReference && instanceProxies.TryGetValue(applicationId, out InstanceProxy instanceProxy))
          {
            converted = instanceProxy;
          }
          else if (_sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference value))
          {
            converted = value;
            cacheHitCount++;
          }
          else
          {
            converted = _converter.Convert(entity);
            converted.applicationId = applicationId;
          }

          // Create and add a collection for each layer if not done so already.
          Layer layer = _layerManager.GetOrCreateSpeckleLayer(entity, tr, out LayerTableRecord? autocadLayer);
          if (autocadLayer is not null)
          {
            layers.Add(autocadLayer);
          }

          modelWithLayers.elements.Add(layer);
          layer.elements.Add(converted);

          results.Add(new(Status.SUCCESS, applicationId, entity.GetType().ToString(), converted));
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          results.Add(new(Status.ERROR, applicationId, entity.GetType().ToString(), null, ex));
          // POC: add logging
        }
        onOperationProgressed?.Invoke("Converting", (double)++count / atomicObjects.Count);
      }

      // POC: Log would be nice, or can be removed.
      Debug.WriteLine(
        $"Cache hit count {cacheHitCount} out of {objects.Count} ({(double)cacheHitCount / objects.Count})"
      );

      var conversionFailedAppIds = results
        .FindAll(result => result.Status == Status.ERROR)
        .Select(result => result.SourceId);

      // Cleans up objects that failed to convert from definition proxies.
      // see https://linear.app/speckle/issue/CNX-115/viewer-handle-gracefully-instances-with-elements-that-failed-to
      foreach (var definitionProxy in instanceDefinitionProxies)
      {
        definitionProxy.objects.RemoveAll(id => conversionFailedAppIds.Contains(id));
      }
      // Set definition proxies
      modelWithLayers["instanceDefinitionProxies"] = instanceDefinitionProxies;

      // set groups
      var groupProxies = _groupUnpacker.UnpackGroups(atomicObjects);
      modelWithLayers["groupProxies"] = groupProxies;

      // set colors
      List<ColorProxy> colorProxies = _colorManager.UnpackColors(atomicObjects, layers);
      modelWithLayers["colorProxies"] = colorProxies;

      return new RootObjectBuilderResult(modelWithLayers, results);
    });
  }
}
