using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.Rhino.Operations.Send;

/// <summary>
/// Stateless builder object to turn an <see cref="ISendFilter"/> into a <see cref="Base"/> object
/// </summary>
public class RhinoRootObjectBuilder : IRootObjectBuilder<RhinoObject>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly RhinoInstanceObjectsManager _instanceObjectsManager;
  private readonly RhinoGroupManager _rhinoGroupManager;
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;
  private readonly RhinoLayerManager _layerManager;
  private readonly RhinoMaterialManager _materialManager;

  public RhinoRootObjectBuilder(
    ISendConversionCache sendConversionCache,
    IConversionContextStack<RhinoDoc, UnitSystem> contextStack,
    RhinoLayerManager layerManager,
    RhinoInstanceObjectsManager instanceObjectsManager,
    RhinoGroupManager rhinoGroupManager,
    IRootToSpeckleConverter rootToSpeckleConverter,
    RhinoMaterialManager materialManager
  )
  {
    _sendConversionCache = sendConversionCache;
    _contextStack = contextStack;
    _layerManager = layerManager;
    _instanceObjectsManager = instanceObjectsManager;
    _rhinoGroupManager = rhinoGroupManager;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _materialManager = materialManager;
  }

  public RootObjectBuilderResult Build(
    IReadOnlyList<RhinoObject> rhinoObjects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken cancellationToken = default
  )
  {
    Collection rootObjectCollection = new() { name = _contextStack.Current.Document.Name ?? "Unnamed document" };
    int count = 0;

    var (atomicObjects, instanceProxies, instanceDefinitionProxies) = _instanceObjectsManager.UnpackSelection(
      rhinoObjects
    );

    // POC: we should formalise this, sooner or later - or somehow fix it a bit more
    rootObjectCollection["instanceDefinitionProxies"] = instanceDefinitionProxies; // this won't work re traversal on receive

    _rhinoGroupManager.UnpackGroups(rhinoObjects);
    rootObjectCollection["groupProxies"] = _rhinoGroupManager.GroupProxies.Values;

    // POC: Handle blocks.
    List<SendConversionResult> results = new(atomicObjects.Count);

    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();

      // handle layer
      Layer layer = _contextStack.Current.Document.Layers[rhinoObject.Attributes.LayerIndex];
      Collection collectionHost = _layerManager.GetHostObjectCollection(layer, rootObjectCollection);
      string applicationId = rhinoObject.Id.ToString();

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
        else if (_sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference value))
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

        results.Add(new(Status.SUCCESS, applicationId, rhinoObject.ObjectType.ToString(), converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        results.Add(new(Status.ERROR, applicationId, rhinoObject.ObjectType.ToString(), null, ex));
      }

      ++count;
      onOperationProgressed?.Invoke("Converting", (double)count / atomicObjects.Count);

      // NOTE: useful for testing ui states, pls keep for now so we can easily uncomment
      // Thread.Sleep(550);
    }

    rootObjectCollection["renderMaterialProxies"] = _materialManager.UnpackRenderMaterial(atomicObjects);

    // 5. profit
    return new(rootObjectCollection, results);
  }
}