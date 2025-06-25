using Rhino.Geometry;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

/// <summary>
/// Handles conversion of atomic objects from TraversalContexts into Grasshopper wrapper objects.
/// </summary>
/// <remarks>
/// Follows Rhino's approach: atomic objects are converted directly without pre-transformation,
/// with instance transformations handled separately during block reconstruction.
/// </remarks>
internal sealed class LocalToGlobalMapHandler
{
  public Dictionary<string, SpeckleObjectWrapper> ConvertedObjectsMap { get; } = new();
  public readonly GrasshopperCollectionRebuilder CollectionRebuilder;

  // TODO: ConsumedObjectIds logic needed - objects consumed by block definitions currently appear as both standalone objects and within blocks.

  private readonly TraversalContextUnpacker _traversalContextUnpacker;
  private readonly GrasshopperColorUnpacker _colorUnpacker;
  private readonly GrasshopperMaterialUnpacker _materialUnpacker;

  public LocalToGlobalMapHandler(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperCollectionRebuilder collectionRebuilder,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorUnpacker = colorUnpacker;
    _materialUnpacker = materialUnpacker;
    CollectionRebuilder = collectionRebuilder;
  }

  /// <summary>
  /// Creates grasshopper speckle objects directly from atomic contexts.
  /// </summary>
  public void ConvertAtomicObject(TraversalContext atomicContext)
  {
    var obj = atomicContext.Current;
    var objId = obj.applicationId ?? obj.id;

    if (objId == null || ConvertedObjectsMap.ContainsKey(objId))
    {
      return;
    }

    try
    {
      List<(GeometryBase, Base)> converted = SpeckleConversionContext.ConvertToHost(obj);

      if (converted.Count == 0)
      {
        return;
      }

      var path = _traversalContextUnpacker.GetCollectionPath(atomicContext).ToList();
      var objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
        path,
        _colorUnpacker,
        _materialUnpacker
      );

      // Extract name and properties
      SpecklePropertyGroupGoo propertyGroup = new();
      string name = "";

      if (obj is Speckle.Objects.Data.DataObject dataObject)
      {
        propertyGroup.CastFrom(dataObject.properties);
        name = dataObject.name;
      }
      else
      {
        if (obj[Constants.PROPERTIES_PROP] is Dictionary<string, object?> props)
        {
          propertyGroup.CastFrom(props);
        }

        if (obj[Constants.NAME_PROP] is string objName)
        {
          name = objName;
        }
      }

      foreach ((GeometryBase geometryBase, Base original) in converted)
      {
        var wrapper = new SpeckleObjectWrapper()
        {
          Base = original,
          Path = path.Select(p => p.name).ToList(),
          Parent = objectCollection,
          GeometryBase = geometryBase,
          Properties = propertyGroup,
          Name = name,
          Color = null,
          Material = null,
          ApplicationId = objId
        };

        ConvertedObjectsMap[objId] = wrapper;
        CollectionRebuilder.AppendSpeckleGrasshopperObject(wrapper, path, _colorUnpacker, _materialUnpacker);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // TODO: throw?
    }
  }

  public void ConvertBlockInstances(
    IReadOnlyCollection<TraversalContext> blocks,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    // GrasshopperBlockUnpacker handles empty inputs, so no need for defensive check here
    var blockUnpacker = new GrasshopperBlockUnpacker(_traversalContextUnpacker, _colorUnpacker, _materialUnpacker);
    blockUnpacker.UnpackBlocks(blocks, definitionProxies, ConvertedObjectsMap, CollectionRebuilder);
  }
}
