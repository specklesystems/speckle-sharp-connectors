using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

public class GrasshopperRootObjectBuilder() : IRootObjectBuilder<SpeckleCollectionWrapperGoo>
{
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

    // deep copy input (to not mutate input) and set the input collection name to "Grasshopper Model"
    var inputCollectionGoo = (SpeckleCollectionWrapperGoo)input[0].Duplicate();
    inputCollectionGoo.Value.Name = "Grasshopper Model";

    // create packers for colors and render materials
    GrasshopperColorPacker colorPacker = new();
    GrasshopperMaterialPacker materialPacker = new();

    // unwrap the input collection to remove all wrappers
    Collection root = Unwrap(inputCollectionGoo.Value, colorPacker, materialPacker);

    // add proxies
    root[ProxyKeys.COLOR] = colorPacker.ColorProxies.Values.ToList();
    root[ProxyKeys.RENDER_MATERIAL] = materialPacker.RenderMaterialProxies.Values.ToList();

    // TODO: Not getting any conversion results yet
    var result = new RootObjectBuilderResult(root, []);

    return Task.FromResult(result);
  }

  // Unwraps collection wrappers and object wrapppers.
  // Also packs colors and Render Materials into proxies while unwrapping.
  private Collection Unwrap(
    SpeckleCollectionWrapper wrapper,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker
  )
  {
    Collection currentColl = wrapper.Collection;

    // unpack color and render material
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
        Unwrap(collWrapper, colorPacker, materialPacker);
      }
      else if (wrapperElement is SpeckleObjectWrapper so)
      {
        // process the object first. This may result in application id mutations, so this must be done before processing color and materials.
        //ProcessObjectWrapper(so, ref collObjectIds);
        DataObject dataObject = ConvertWrappersToDataObject(
          new List<SpeckleObjectWrapper>() { so },
          Guid.NewGuid().ToString() // note: we are always generating a new id here, do *not* use the Base appid as this will cause conflicts in viewer for color and material proxy application
        );
        currentColl.elements.Add(dataObject);

        // unpack color and render material
        colorPacker.ProcessColor(so.ApplicationId, so.Color);
        materialPacker.ProcessMaterial(so.ApplicationId, so.Material);
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

  // creates a data object from the input wrappers.
  // assumes these wrappers have been processed for similarity, so that the name and props of all wrappers are the same.
  private DataObject ConvertWrappersToDataObject(List<SpeckleObjectWrapper> wrappers, string appId)
  {
    Dictionary<string, object?> props = new();
    wrappers.First().Properties.CastTo<Dictionary<string, object?>>(ref props);

    return new()
    {
      displayValue = wrappers.Select(o => o.Base).ToList(),
      name = wrappers.First().Name,
      properties = props,
      applicationId = appId
    };
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
