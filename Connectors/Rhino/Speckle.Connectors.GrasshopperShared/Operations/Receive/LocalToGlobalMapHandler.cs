using Rhino.Geometry;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Common.ToHost;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

/// <summary>
/// Handles conversion of atomic objects from TraversalContexts into Grasshopper wrapper objects.
/// </summary>
/// <remarks>
/// Follows Rhino's approach: atomic objects are converted directly without pre-transformation,
/// with instance transformations handled separately during block reconstruction. Implements consumedObjectIds
/// tracking to prevent objects consumed by block definitions from appearing as standalone objects.
/// </remarks>
internal sealed class LocalToGlobalMapHandler
{
  public Dictionary<string, SpeckleGeometryWrapper> ConvertedObjectsMap { get; } = new();
  public readonly GrasshopperCollectionRebuilder CollectionRebuilder;

  private readonly TraversalContextUnpacker _traversalContextUnpacker;
  private readonly GrasshopperColorUnpacker _colorUnpacker;
  private readonly GrasshopperMaterialUnpacker _materialUnpacker;
  private readonly IDataObjectInstanceRegistry _dataObjectInstanceRegistry;
  private readonly IReadOnlyCollection<InstanceDefinitionProxy>? _definitionProxies;

  public LocalToGlobalMapHandler(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperCollectionRebuilder collectionRebuilder,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker,
    IDataObjectInstanceRegistry dataObjectInstanceRegistry,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorUnpacker = colorUnpacker;
    _materialUnpacker = materialUnpacker;
    _dataObjectInstanceRegistry = dataObjectInstanceRegistry;
    _definitionProxies = definitionProxies;
    CollectionRebuilder = collectionRebuilder;
  }

  /// <summary>
  /// Converts atomic object from TraversalContext to SpeckleObjectWrapper.
  /// </summary>
  /// <summary>
  /// Converts atomic object from TraversalContext to SpeckleObjectWrapper.
  /// Skips registered DataObjects - those are handled in ConvertRegisteredDataObject.
  /// </summary>
  public void ConvertAtomicObject(TraversalContext atomicContext)
  {
    var obj = atomicContext.Current;
    var objId = obj.applicationId ?? obj.id;

    if (objId == null || ConvertedObjectsMap.ContainsKey(objId))
    {
      return;
    }

    // Skip registered DataObjects - they'll be processed in second pass
    if (
      obj is Speckle.Objects.Data.DataObject dataObject
      && _dataObjectInstanceRegistry.IsRegistered(dataObject.applicationId ?? dataObject.id.NotNull())
    )
    {
      return;
    }

    try
    {
      List<(object, Base)> converted = SpeckleConversionContext.Current.ConvertToHost(obj);

      // get path and collection
      var path = _traversalContextUnpacker.GetCollectionPath(atomicContext).ToList();
      var objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
        path,
        _colorUnpacker,
        _materialUnpacker
      );

      // nothing converted - nothing to do
      if (converted.Count == 0)
      {
        return;
      }

      // handle normal DataObject (has converted geometry)
      if (obj is Speckle.Objects.Data.DataObject normalDataObject)
      {
        var geometries = ConvertToGeometryWrappers(converted);
        var dataObjectWrapper = CreateDataObjectWrapper(normalDataObject, geometries, path, objectCollection);

        CollectionRebuilder.AppendSpeckleGrasshopperObject(dataObjectWrapper, path, _colorUnpacker, _materialUnpacker);
        return;
      }

      // handle normal geometry (not DataObject)
      SpecklePropertyGroupGoo propertyGroup = new();
      if (obj[Constants.PROPERTIES_PROP] is Dictionary<string, object?> props)
      {
        propertyGroup.CastFrom(props);
      }

      foreach ((object convertedObj, Base original) in converted)
      {
        if (convertedObj is GeometryBase geometryBase)
        {
          var wrapper = new SpeckleGeometryWrapper()
          {
            Base = original,
            Path = path.Select(p => p.name).ToList(),
            Parent = objectCollection,
            GeometryBase = geometryBase,
            Properties = propertyGroup,
            Name = obj[Constants.NAME_PROP] as string ?? "",
            Color = _colorUnpacker.Cache.TryGetValue(original.applicationId ?? "", out var cachedObjColor)
              ? cachedObjColor
              : null,
            Material = _materialUnpacker.Cache.TryGetValue(original.applicationId ?? "", out var cachedObjMaterial)
              ? cachedObjMaterial
              : null,
            ApplicationId = objId
          };

          ConvertedObjectsMap[objId] = wrapper;
          CollectionRebuilder.AppendSpeckleGrasshopperObject(wrapper, path, _colorUnpacker, _materialUnpacker);
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // TODO: throw?
    }
  }

  /// <summary>
  /// Processes a registered DataObject with InstanceProxy displayValues.
  /// Should be called AFTER all atomic objects are converted so definition objects are available.
  /// </summary>
  public void ConvertRegisteredDataObject(TraversalContext atomicContext)
  {
    var obj = atomicContext.Current;
    if (obj is not Speckle.Objects.Data.DataObject dataObject)
    {
      return;
    }

    var dataObjectId = dataObject.applicationId ?? dataObject.id.NotNull();
    if (!_dataObjectInstanceRegistry.IsRegistered(dataObjectId))
    {
      return;
    }

    try
    {
      var path = _traversalContextUnpacker.GetCollectionPath(atomicContext).ToList();
      var objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
        path,
        _colorUnpacker,
        _materialUnpacker
      );

      var entry = _dataObjectInstanceRegistry.GetEntries()[dataObjectId];
      var resolvedGeometries = ResolveInstanceProxiesToGeometries(entry.InstanceProxies);
      var dataObjectWrapper = CreateDataObjectWrapper(dataObject, resolvedGeometries, path, objectCollection);

      CollectionRebuilder.AppendSpeckleGrasshopperObject(dataObjectWrapper, path, _colorUnpacker, _materialUnpacker);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // TODO: throw?
    }
  }

  /// <summary>
  /// Converts block instances and definitions from traversal contexts into Grasshopper wrapper objects.
  /// Automatically handles cleanup of consumed objects from the collection hierarchy.
  /// </summary>
  /// <remarks>
  /// Deliberately handles both block conversion AND consumed object cleanup in a single operation.
  /// Too much, I know, BUT it ensures the cleanup always occurs immediately after block processing without
  /// requiring receive components to call a separate cleanup method in the correct order.
  /// </remarks>
  public void ConvertBlockInstances(
    IReadOnlyCollection<TraversalContext> blocks,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    var blockUnpacker = new GrasshopperBlockUnpacker(_traversalContextUnpacker, _colorUnpacker, _materialUnpacker);

    // Get consumed object IDs from unpacker
    var consumedObjectIds = blockUnpacker.UnpackBlocks(
      blocks,
      definitionProxies,
      ConvertedObjectsMap,
      CollectionRebuilder
    );

    // Clean up consumed objects from collections
    CollectionRebuilder.RemoveConsumedObjects(consumedObjectIds);
  }

  /// <summary>
  /// Creates a DataObjectWrapper from a DataObject and its geometries.
  /// Handles color/material inheritance and property extraction.
  /// </summary>
  private SpeckleDataObjectWrapper CreateDataObjectWrapper(
    Speckle.Objects.Data.DataObject dataObject,
    List<SpeckleGeometryWrapper> geometries,
    List<Collection> path,
    SpeckleCollectionWrapper objectCollection
  )
  {
    // Get color and material on DataObject
    Color? dataObjColor = _colorUnpacker.Cache.TryGetValue(dataObject.applicationId ?? "", out var cachedDataObjColor)
      ? cachedDataObjColor
      : null;

    SpeckleMaterialWrapper? dataObjMat = _materialUnpacker.Cache.TryGetValue(
      dataObject.applicationId ?? "",
      out var cachedDataObjMaterial
    )
      ? cachedDataObjMaterial
      : null;

    // Apply DataObject color/material to geometries that don't have their own
    foreach (var geometry in geometries)
    {
      geometry.Color ??= dataObjColor;
      geometry.Material ??= dataObjMat;
    }

    // Create property group
    SpecklePropertyGroupGoo propertyGroup = new();
    propertyGroup.CastFrom(dataObject.properties);

    // Clear the displayValue to prevent storing duplicate Base
    dataObject.displayValue.Clear();

    return new SpeckleDataObjectWrapper()
    {
      Base = dataObject,
      Geometries = geometries,
      Path = path.Select(p => p.name).ToList(),
      Parent = objectCollection,
      Name = dataObject.name,
      Properties = propertyGroup,
      ApplicationId = dataObject.applicationId,
    };
  }

  /// <summary>
  /// Resolves InstanceProxy displayValues to transformed geometries.
  /// Returns the list of resolved geometries that can be used as DataObject displayValue replacements.
  /// </summary>
  private List<SpeckleGeometryWrapper> ResolveInstanceProxiesToGeometries(List<InstanceProxy> instanceProxies)
  {
    var resolvedGeometries = new List<SpeckleGeometryWrapper>();

    // build a lookup of definitionId -> definition objects for quick access
    var definitionObjectsMap = new Dictionary<string, List<string>>();
    if (_definitionProxies != null)
    {
      foreach (var defProxy in _definitionProxies)
      {
        var defId = defProxy.applicationId ?? defProxy.id;
        if (defId != null)
        {
          definitionObjectsMap[defId] = defProxy.objects;
        }
      }
    }

    // DEBUG: Check what's in ConvertedObjectsMap
    Console.WriteLine($"ConvertedObjectsMap has {ConvertedObjectsMap.Count} entries");
    foreach (var key in ConvertedObjectsMap.Keys.Take(5))
    {
      Console.WriteLine($"  - {key}");
    }

    foreach (var instanceProxy in instanceProxies)
    {
      Console.WriteLine($"Processing InstanceProxy with definitionId: {instanceProxy.definitionId}");

      // get the definition objects for this instance
      if (!definitionObjectsMap.TryGetValue(instanceProxy.definitionId, out var definitionObjectIds))
      {
        Console.WriteLine($"  Definition not found in definitionObjectsMap");
        continue; // definition not found, skip this proxy
      }

      Console.WriteLine($"  Definition has {definitionObjectIds.Count} objects");

      // get transform from the instance proxy
      var transform = GrasshopperHelpers.MatrixToTransform(instanceProxy.transform, instanceProxy.units);

      // apply transform to each definition object
      foreach (var objectId in definitionObjectIds)
      {
        Console.WriteLine($"    Looking for object: {objectId}");
        if (ConvertedObjectsMap.TryGetValue(objectId, out var definitionObject))
        {
          Console.WriteLine($"      Found! Creating transformed copy");
          // deep copy and transform the geometry
          var transformedWrapper = definitionObject.DeepCopy();
          if (transformedWrapper.GeometryBase != null)
          {
            transformedWrapper.GeometryBase.Transform(transform);
          }
          resolvedGeometries.Add(transformedWrapper);
        }
        else
        {
          Console.WriteLine($"      NOT FOUND in ConvertedObjectsMap");
        }
      }
    }

    Console.WriteLine($"Resolved {resolvedGeometries.Count} total geometries");
    return resolvedGeometries;
  }

  /// <summary>
  /// Converts the raw converted objects to SpeckleGeometryWrappers for DataObject display values.
  /// Does NOT apply DataObject-level colors/materials - that's handled by CreateDataObjectWrapper.
  /// </summary>
  private List<SpeckleGeometryWrapper> ConvertToGeometryWrappers(List<(object, Base)> converted)
  {
    var geometries = new List<SpeckleGeometryWrapper>();

    foreach ((object convertedObj, Base original) in converted)
    {
      if (convertedObj is GeometryBase geometryBase)
      {
        SpeckleGeometryWrapper wrapper =
          new()
          {
            Base = original,
            GeometryBase = geometryBase,
            // try to get color/material from the individual geometry first
            Color = _colorUnpacker.Cache.TryGetValue(original.applicationId ?? "", out var cachedObjColor)
              ? cachedObjColor
              : null,
            Material = _materialUnpacker.Cache.TryGetValue(original.applicationId ?? "", out var cachedObjMaterial)
              ? cachedObjMaterial
              : null,
          };

        geometries.Add(wrapper);
      }
    }

    return geometries;
  }
}
