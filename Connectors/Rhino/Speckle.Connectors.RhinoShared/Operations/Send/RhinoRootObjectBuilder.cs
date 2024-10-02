using Microsoft.Extensions.Logging;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.Rhino.Operations.Send;

/// <summary>
/// Stateless builder object to turn an <see cref="ISendFilter"/> into a <see cref="Base"/> object
/// </summary>
public class RhinoRootObjectBuilder : IRootObjectBuilder<RhinoObject>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly RhinoLayerUnpacker _layerUnpacker;
  private readonly RhinoInstanceUnpacker _instanceUnpacker;
  private readonly RhinoGroupUnpacker _groupUnpacker;
  private readonly RhinoMaterialUnpacker _materialUnpacker;
  private readonly RhinoColorUnpacker _colorUnpacker;
  private readonly ILogger<RhinoRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  public RhinoRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    RhinoLayerUnpacker layerUnpacker,
    RhinoInstanceUnpacker instanceUnpacker,
    RhinoGroupUnpacker groupUnpacker,
    RhinoMaterialUnpacker materialUnpacker,
    RhinoColorUnpacker colorUnpacker,
    ILogger<RhinoRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory
  )
  {
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _layerUnpacker = layerUnpacker;
    _instanceUnpacker = instanceUnpacker;
    _groupUnpacker = groupUnpacker;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _materialUnpacker = materialUnpacker;
    _colorUnpacker = colorUnpacker;
    _logger = logger;
    _activityFactory = activityFactory;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<RhinoObject> rhinoObjects,
    SendInfo sendInfo,
    IProgress<ProgressAction> onOperationProgressed,
    CancellationToken cancellationToken = default
  )
  {
    using var activity = _activityFactory.Start("Build");
    // 0 - Init the root
    Collection rootObjectCollection = new() { name = _converterSettings.Current.Document.Name ?? "Unnamed document" };
    rootObjectCollection["units"] = _converterSettings.Current.SpeckleUnits;

    // 1 - Unpack the instances
    UnpackResult<RhinoObject> unpackResults;
    using (var _ = _activityFactory.Start("UnpackSelection"))
    {
      unpackResults = _instanceUnpacker.UnpackSelection(rhinoObjects);
    }

    var (atomicObjects, instanceProxies, instanceDefinitionProxies) = unpackResults;
    // POC: we should formalise this, sooner or later - or somehow fix it a bit more
    rootObjectCollection[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies; // this won't work re traversal on receive

    // 2 - Unpack the groups
    _groupUnpacker.UnpackGroups(rhinoObjects);
    rootObjectCollection[ProxyKeys.GROUP] = _groupUnpacker.GroupProxies.Values;

    // 3 - Convert atomic objects
    List<SendConversionResult> results = new(atomicObjects.Count);
    HashSet<Layer> versionLayers = new();
    int count = 0;
    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (RhinoObject rhinoObject in atomicObjects)
      {
        using var _2 = _activityFactory.Start("Convert");
        cancellationToken.ThrowIfCancellationRequested();

        // handle layer
        Layer layer = _converterSettings.Current.Document.Layers[rhinoObject.Attributes.LayerIndex];
        versionLayers.Add(layer);
        Collection collectionHost = _layerUnpacker.GetHostObjectCollection(layer, rootObjectCollection);

        var result = ConvertRhinoObject(rhinoObject, collectionHost, instanceProxies, sendInfo.ProjectId);
        results.Add(result);

        ++count;
         onOperationProgressed.Report(new("Converting", (double)count / atomicObjects.Count));

        // NOTE: useful for testing ui states, pls keep for now so we can easily uncomment
        // Thread.Sleep(550);
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleConversionException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
    }

    using (var _ = _activityFactory.Start("UnpackRenderMaterials"))
    {
      // 4 - Unpack the render material proxies
      rootObjectCollection[ProxyKeys.RENDER_MATERIAL] = _materialUnpacker.UnpackRenderMaterial(atomicObjects);

      // 5 - Unpack the color proxies
      rootObjectCollection[ProxyKeys.COLOR] = _colorUnpacker.UnpackColors(atomicObjects, versionLayers.ToList());
    }

    await Task.Yield();
    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private SendConversionResult ConvertRhinoObject(
    RhinoObject rhinoObject,
    Collection collectionHost,
    IReadOnlyDictionary<string, InstanceProxy> instanceProxies,
    string projectId
  )
  {
    string applicationId = rhinoObject.Id.ToString();
    string sourceType = rhinoObject.ObjectType.ToString();
    try
    {
      // get from cache or convert:
      // What we actually do here is check if the object has been previously converted AND has not changed.
      // If that's the case, we insert in the host collection just its object reference which has been saved from the prior conversion.
      Base converted;
      if (rhinoObject is InstanceObject)
      {
        converted = instanceProxies[applicationId];
      }
      else if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
      {
        converted = value;
      }
      else
      {
        converted = _rootToSpeckleConverter.Convert(rhinoObject);
        converted.applicationId = applicationId;
      }

      // add to host
      collectionHost.elements.Add(converted);

      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogSendConversionError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
