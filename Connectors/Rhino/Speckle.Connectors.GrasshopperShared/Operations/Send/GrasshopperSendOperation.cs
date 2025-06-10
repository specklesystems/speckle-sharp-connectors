using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

public class GrasshopperRootObjectBuilder : IRootObjectBuilder<SpeckleCollectionWrapperGoo>
{
  // Keeps track of the wrapper applicationId of processed objects for send.
  // This is used to keep track of the following situations:
  // 1 - objects with the same name, properties, and application id are packaged into a data object. this can happen when receiving data objects.
  // 2 - mutated objects (put into a diff collection) that originally came from the same display value should be assigned a new application id.
  // note: if any original objects that came from the same display value were mutated in geometry, props, or name, in the create speckle object node, they will already be assigned a new appId
  //private readonly Dictionary<string, List<SpeckleObjectWrapper>> _applicationIdCache = new();

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<SpeckleCollectionWrapperGoo> input,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // TODO: Send info is used in other connectors to get the project ID to populate the SendConversionCache
    Console.WriteLine($"Send Info {sendInfo}");

    // deep copy input (to not mutate input) and set the input collection name to "Grasshopper Model"
    var inputCollectionGoo = (SpeckleCollectionWrapperGoo)input[0].Duplicate();
    inputCollectionGoo.Value.Name = "Grasshopper Model";

    // create packers for colors and render materials
    GrasshopperColorPacker colorPacker = new();
    GrasshopperMaterialPacker materialPacker = new();
    GrasshopperBlockPacker blockPacker = new();

    // unwrap the input collection to remove all wrappers
    Collection root = Unwrap(inputCollectionGoo.Value, colorPacker, materialPacker, blockPacker);

    // add proxies
    root[ProxyKeys.COLOR] = colorPacker.ColorProxies.Values.ToList();
    root[ProxyKeys.RENDER_MATERIAL] = materialPacker.RenderMaterialProxies.Values.ToList();
    root[ProxyKeys.INSTANCE_DEFINITION] = blockPacker.InstanceDefinitionProxies.Values.ToList();

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(root, []);

    return Task.FromResult(result);
  }

  // Unwraps collection wrappers and object wrappers.
  // Also packs colors, render materials and block definitions into proxies while unwrapping.
  private Collection Unwrap(
    SpeckleCollectionWrapper wrapper,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker,
    GrasshopperBlockPacker blockPacker
  )
  {
    Collection currentColl = wrapper.Collection;

    // unpack color, render material and block definitions
    colorPacker.ProcessColor(wrapper.ApplicationId, wrapper.Color);
    materialPacker.ProcessMaterial(wrapper.ApplicationId, wrapper.Material);

    // iterate through this wrapper's elements to unwrap children
    // HashSet<string> collObjectIds = new();
    foreach (SpeckleWrapper wrapperElement in wrapper.Elements)
    {
      if (wrapperElement is SpeckleCollectionWrapper collWrapper)
      {
        // create an application id for this collection if none exists. This will be used for color and render material proxies
        collWrapper.ApplicationId ??= collWrapper.GetSpeckleApplicationId();

        // add to collection and continue unwrap
        currentColl.elements.Add(collWrapper.Collection);
        Unwrap(collWrapper, colorPacker, materialPacker, blockPacker);
      }
      else if (wrapperElement is SpeckleObjectWrapper so)
      {
        // process the object first. This may result in application id mutations, so this must be done before processing color and materials.
        //ProcessObjectWrapper(so, ref collObjectIds);
        Base objectBase = ConvertWrapperToBase(so);
        currentColl.elements.Add(objectBase);

        // unpack color and render material
        colorPacker.ProcessColor(so.ApplicationId, so.Color);
        materialPacker.ProcessMaterial(so.ApplicationId, so.Material);
      }
      else if (wrapperElement is SpeckleBlockInstanceWrapper bi)
      {
        // process block instances - they get added to collection as DataObject
        Base blockInstanceBase = ConvertWrapperToBase(bi);
        currentColl.elements.Add(blockInstanceBase);

        // process block for definition collection and get defining objects
        var definitionObjects = blockPacker.ProcessInstance(bi);

        if (definitionObjects != null)
        {
          foreach (var definitionObject in definitionObjects)
          {
            Base defObjectBase = ConvertWrapperToBase(definitionObject);

            // just add to current collection
            // TODO: where on collection?
            currentColl.elements.Add(defObjectBase);

            colorPacker.ProcessColor(definitionObject.ApplicationId, definitionObject.Color);
            materialPacker.ProcessMaterial(definitionObject.ApplicationId, definitionObject.Material);
          }
        }
      }
    }

    /*
    // now package all corresponding wrappers of app ids in the hashset into dataobjects, and add to collection
    foreach (string collObjectId in collObjectIds)
    {
      if (_applicationIdCache.TryGetValue(collObjectId, out var wrappers))
      {
        DataObject dataObject = ConvertWrappersToDataObject(wrappers, collObjectId);
        currentColl.elements.Add(dataObject);
      }
    }
    */

    return currentColl;
  }

  /// <summary>
  /// Converts a <see cref="SpeckleWrapper"/> to underlying Base object with dynamically attached properties.
  /// </summary>
  /// <remarks>
  /// Only intended for <see cref="SpeckleObjectWrapper"/> and <see cref="SpeckleBlockInstanceWrapper"/>!
  /// </remarks>
  private Base ConvertWrapperToBase(SpeckleWrapper wrapper)
  {
    Dictionary<string, object?> props = [];

    var properties = wrapper switch
    {
      SpeckleObjectWrapper obj => obj.Properties,
      SpeckleBlockInstanceWrapper instance => instance.Properties,
      _ => throw new ArgumentException($"Unsupported wrapper type: {wrapper.GetType().Name}")
    };
    properties.CastTo(ref props);

    Base baseObject = wrapper.Base;
    baseObject["name"] = wrapper.Name;
    baseObject["properties"] = props;
    baseObject.applicationId ??= Guid.NewGuid().ToString();

    return baseObject;
  }

  /*
  // will cache the object wrappers and group them by similarity.
  private void ProcessObjectWrapper(SpeckleObjectWrapper objectWrapper, ref HashSet<string> processedIds)
  {
    // check each of the hashset keys in the cache for similarity to this objectwrapper
    foreach (string processedId in processedIds)
    {
      if (_applicationIdCache.TryGetValue(processedId, out List<SpeckleObjectWrapper> wrappers))
      {
        // check if the object wrapper smells like existing object wrappers.
        if (objectWrapper.SmellsLike(wrappers.FirstOrDefault()))
        {
          objectWrapper.WrapperGuid = processedId;
          _applicationIdCache[processedId].Add(objectWrapper);
          return;
        }
      }
    }

    // if no similar wrappers found, store this id (or create new one if it doesnt exist).
    objectWrapper.WrapperGuid = objectWrapper.Base.applicationId ?? Guid.NewGuid().ToString();
    processedIds.Add(objectWrapper.WrapperGuid);
    _applicationIdCache.Add(objectWrapper.WrapperGuid, new() { objectWrapper });
    return;
  }
  */
}
