using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.Rhino7.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using RenderMaterialProxy = Objects.Other.RenderMaterialProxy;

namespace Speckle.Connectors.Rhino7.Operations.Send;

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
    Dictionary<string, List<string>> renderMaterialToObjectIdMap = new();
    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();

      // handle layer
      Rhino.DocObjects.Layer layer = _contextStack.Current.Document.Layers[rhinoObject.Attributes.LayerIndex];
      Collection collectionHost = _layerManager.GetHostObjectCollection(layer, rootObjectCollection);

      // handle render materials
      // POC: we are only adding the object to the render material map if its material source is set to from object.
      // POC: this means that most versions will not contain the default material, unless we decide to explicitly send it.
      if (rhinoObject.Attributes.MaterialSource == ObjectMaterialSource.MaterialFromObject)
      {
        string? renderMaterialId = null;
        if (rhinoObject.GetRenderMaterial(true) is Rhino.Render.RenderMaterial objRenderMaterial)
        {
          _materialManager.CreateSpeckleRenderMaterial(objRenderMaterial);
          renderMaterialId = objRenderMaterial.Id.ToString();
        }
        else if (rhinoObject.GetMaterial(true) is Rhino.DocObjects.Material objMaterial)
        {
          _materialManager.CreateSpeckleRenderMaterial(objMaterial);
          renderMaterialId = objMaterial.Id.ToString();
        }
        else
        {
          continue;
          // TODO: report error, couldn't retrieve object render material or material
        }

        if (renderMaterialToObjectIdMap.TryGetValue(renderMaterialId, out List<string> objectList))
        {
          objectList.Add(rhinoObject.Id.ToString());
        }
        else
        {
          renderMaterialToObjectIdMap[renderMaterialId] = [rhinoObject.Id.ToString()];
        }
      }

      if (layer.RenderMaterial is Rhino.Render.RenderMaterial layerMaterial)
      {
        _materialManager.CreateSpeckleRenderMaterial(layerMaterial);

        if (renderMaterialToObjectIdMap.TryGetValue(layerMaterial.Id.ToString(), out List<string> objectList))
        {
          objectList.Add(rhinoObject.Id.ToString());
        }
        else
        {
          renderMaterialToObjectIdMap[layerMaterial.Id.ToString()] = [rhinoObject.Id.ToString()];
        }
      }

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
          ;
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

    // POC: Create render material proxies and add to root collection
    List<RenderMaterialProxy> renderMaterialProxies = new();
    foreach (string matId in _materialManager.SpeckleRenderMaterials.Keys)
    {
      if (renderMaterialToObjectIdMap.TryGetValue(matId, out List<string> objIds))
      {
        renderMaterialProxies.Add(new(_materialManager.SpeckleRenderMaterials[matId], objIds));
      }
    }

    rootObjectCollection["renderMaterialProxies"] = renderMaterialProxies;

    // 5. profit
    return new(rootObjectCollection, results);
  }
}
