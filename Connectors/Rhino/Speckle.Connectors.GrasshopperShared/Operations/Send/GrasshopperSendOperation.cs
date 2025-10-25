using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

public class GrasshopperRootObjectBuilder : IRootObjectBuilder<SpeckleCollectionWrapperGoo>
{
  private readonly IInstanceObjectsManager<SpeckleGeometryWrapper, List<string>> _instanceObjectsManager;

  // each Build() call gets a fresh scoped IInstanceObjectsManager
  public GrasshopperRootObjectBuilder(
    IInstanceObjectsManager<SpeckleGeometryWrapper, List<string>> instanceObjectsManager
  )
  {
    _instanceObjectsManager = instanceObjectsManager;
  }

  // Keeps track of the wrapper applicationId of processed objects for send.
  // This is used to keep track of the following situations:
  // 1 - objects with the same name, properties, and application id are packaged into a data object. this can happen when receiving data objects.
  // 2 - mutated objects (put into a diff collection) that originally came from the same display value should be assigned a new application id.
  // note: if any original objects that came from the same display value were mutated in geometry, props, or name, in the create speckle object node, they will already be assigned a new appId
  //private readonly Dictionary<string, List<SpeckleObjectWrapper>> _applicationIdCache = new();

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<SpeckleCollectionWrapperGoo> input,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    // create root collection
    var rootCollectionGoo = (SpeckleRootCollectionWrapperGoo)input[0].Duplicate();
    rootCollectionGoo.Value.Name = "Grasshopper Model";
    RootCollection rootCollection = new(rootCollectionGoo.Value.Name)
    {
      applicationId = rootCollectionGoo.Value.ApplicationId, rootProperties = rootCollectionGoo.Value.RootProperties
    };

    // create packers for colors and render materials
    GrasshopperColorPacker colorPacker = new();
    GrasshopperMaterialPacker materialPacker = new();
    GrasshopperBlockPacker blockPacker = new(_instanceObjectsManager);
    
    // unwrap the input collection to remove all wrappers
    Unwrap(rootCollectionGoo.Value, rootCollection, colorPacker, materialPacker, blockPacker);

    // add proxies
    rootCollection[ProxyKeys.COLOR] = colorPacker.ColorProxies.Values.ToList();
    rootCollection[ProxyKeys.RENDER_MATERIAL] = materialPacker.RenderMaterialProxies.Values.ToList();
    rootCollection[ProxyKeys.INSTANCE_DEFINITION] = blockPacker.InstanceDefinitionProxies.Values.ToList();

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(rootCollection, []);

    return Task.FromResult(result);
  }

  // Unwraps collection wrappers and object wrappers.
  // Also packs colors, render materials and block definitions into proxies while unwrapping.
  private Collection Unwrap(
    SpeckleCollectionWrapper wrapper,
    Collection targetCollection,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker,
    GrasshopperBlockPacker blockPacker
  )
  {
    // unpack color, render material and block definitions
    colorPacker.ProcessColor(wrapper.ApplicationId, wrapper.Color);
    materialPacker.ProcessMaterial(wrapper.ApplicationId, wrapper.Material);

    // iterate through this wrapper's elements to unwrap children
    // HashSet<string> collObjectIds = new();
    foreach (ISpeckleCollectionObject element in wrapper.Elements)
    {
      switch (element)
      {
        case SpeckleCollectionWrapper collWrapper:
          // create an application id for this collection if none exists. This will be used for color and render material proxies
          collWrapper.ApplicationId ??= collWrapper.GetSpeckleApplicationId();

          // add to collection and continue unwrap
          targetCollection.elements.Add(collWrapper.Collection);
          Unwrap(collWrapper, collWrapper.Collection, colorPacker, materialPacker, blockPacker);
          break;

        case SpeckleGeometryWrapper so: // handles both SpeckleObjectWrapper and SpeckleBlockInstanceWrapper (inheritance)
          // convert wrapper to base and add to collection - common for all object wrappers
          Base objectBase = UnwrapGeometry(so);
          string applicationId = objectBase.applicationId!;
          targetCollection.elements.Add(objectBase);

          // do block instance specific stuff (if this object wrapper is actually a block instance)
          if (so is SpeckleBlockInstanceWrapper blockInstance)
          {
            ProcessBlockInstanceDefinition(blockInstance, colorPacker, materialPacker, blockPacker, targetCollection);
          }

          // process color and material for all object wrappers (including block instances)
          colorPacker.ProcessColor(applicationId, so.Color);
          materialPacker.ProcessMaterial(applicationId, so.Material);
          break;

        case SpeckleDataObjectWrapper dataObjectWrapper:
          // convert wrapper to DataObject and add to collection
          // UnwrapDataObject will unwrap underlying geometry and handle color and material
          // arguably doing too much, but I'm apprehensive looping twice without good reason
          DataObject dataObject = UnwrapDataObject(dataObjectWrapper, colorPacker, materialPacker);
          targetCollection.elements.Add(dataObject);
          break;
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

    return targetCollection;
  }

  /// <summary>
  /// Converts a <see cref="SpeckleGeometryWrapper"/> to underlying Base object with dynamically attached properties.
  /// </summary>
  /// <remarks>
  /// POC: if we move properties assignment to auto set the wrapped base, we can get rid of this entirely!
  /// </remarks>
  private Base UnwrapGeometry(SpeckleGeometryWrapper wrapper)
  {
    Dictionary<string, object?> props = [];
    Base baseObject = wrapper.Base;
    if (wrapper.Properties.CastTo(ref props))
    {
      baseObject["properties"] = props; // setting props here on base since it's not auto-set, like name and appid
    }

    return baseObject;
  }

  /// <summary>
  /// Processes a block instance's definition and adds the defining objects to the current collection.
  /// Handles nested block hierarchies and depth calculation via GrasshopperBlockPacker.
  /// </summary>
  private void ProcessBlockInstanceDefinition(
    SpeckleBlockInstanceWrapper blockInstance,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker,
    GrasshopperBlockPacker blockPacker,
    Collection currentColl
  )
  {
    // NOTE: Depth calculation handled by GrasshopperBlockPacker.ProcessInstance()
    // Objects start with maxDepth=0, then updated during processing

    // process block for definition collection and get defining objects
    var definitionObjects = blockPacker.ProcessInstance(blockInstance);

    if (definitionObjects != null)
    {
      foreach (var definitionObject in definitionObjects)
      {
        Base defObjectBase = UnwrapGeometry(definitionObject);
        string applicationId = defObjectBase.applicationId!;

        // just add to current collection for now
        currentColl.elements.Add(defObjectBase);

        colorPacker.ProcessColor(applicationId, definitionObject.Color);
        materialPacker.ProcessMaterial(applicationId, definitionObject.Material);
      }
    }
  }

  /// <summary>
  /// Converts a <see cref="SpeckleDataObjectWrapper"/> to underlying DataObject with properly configured displayValue.
  /// Processes colors and materials for each individual geometry during conversion.
  /// </summary>
  private DataObject UnwrapDataObject(
    SpeckleDataObjectWrapper wrapper,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker
  )
  {
    DataObject dataObject = wrapper.DataObject;

    // Convert geometries back to Base objects for displayValue
    var displayValue = new List<Base>();
    foreach (var geometryWrapper in wrapper.Geometries)
    {
      Base geometryBase = UnwrapGeometry(geometryWrapper);
      displayValue.Add(geometryBase);

      // process color and material for each geometry while we're iterating
      // this could be in the switch statements (like SpeckleGeometryWrapper) but then we're unnecessarily looping twice
      if (geometryWrapper.ApplicationId != null)
      {
        colorPacker.ProcessColor(geometryWrapper.ApplicationId, geometryWrapper.Color);
        materialPacker.ProcessMaterial(geometryWrapper.ApplicationId, geometryWrapper.Material);
      }
    }

    // Update the DataObject's displayValue
    dataObject.displayValue = displayValue;

    return dataObject;
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
