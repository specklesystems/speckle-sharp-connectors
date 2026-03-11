using Microsoft.Extensions.Logging;
using Rhino.Geometry;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Common;
using Speckle.Converters.Common.ToHost;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

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

  // injected via constructor (DI-managed)
  private readonly IDataObjectInstanceRegistry _dataObjectInstanceRegistry;
  private readonly ILogger<LocalToGlobalMapHandler> _logger;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  // set via Initialize() method (per-operation data)
  private TraversalContextUnpacker _traversalContextUnpacker = null!;
  private GrasshopperColorUnpacker _colorUnpacker = null!;
  private GrasshopperMaterialUnpacker _materialUnpacker = null!;
  private IReadOnlyCollection<InstanceDefinitionProxy>? _definitionProxies;

  // auto property (fixes IDE0032)
  public GrasshopperCollectionRebuilder CollectionRebuilder { get; private set; } = null!;

  public LocalToGlobalMapHandler(
    IDataObjectInstanceRegistry dataObjectInstanceRegistry,
    ILogger<LocalToGlobalMapHandler> logger,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _dataObjectInstanceRegistry = dataObjectInstanceRegistry;
    _logger = logger;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Initializes the handler with per-operation data.
  /// Must be called before using ConvertAtomicObjects or ConvertBlockInstances.
  /// </summary>
  public LocalToGlobalMapHandler Initialize(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker,
    GrasshopperCollectionRebuilder collectionRebuilder,
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorUnpacker = colorUnpacker;
    _materialUnpacker = materialUnpacker;
    CollectionRebuilder = collectionRebuilder;
    _definitionProxies = definitionProxies;

    return this;
  }

  /// <summary>
  /// Converts all atomic objects in two passes:
  /// Pass 1 - Convert normal objects and populate ConvertedObjectsMap
  /// Pass 2 - Resolve registered DataObjects with InstanceProxies using the populated map
  /// </summary>
  public void ConvertAtomicObjects(IEnumerable<TraversalContext> atomicContexts)
  {
    // Cache to avoid re-iterating for registered check
    var atomicList = atomicContexts as IList<TraversalContext> ?? atomicContexts.ToList();

    // Pass 1: Convert all non-registered DataObjects to populate ConvertedObjectsMap
    foreach (var atomicContext in atomicList)
    {
      ConvertObjectToCache(atomicContext);
    }

    // Pass 2: Process registered DataObjects (definitions now available in ConvertedObjectsMap)
    foreach (var atomicContext in atomicList)
    {
      if (atomicContext.Current is DataObject dataObject)
      {
        var dataObjectId = dataObject.applicationId ?? dataObject.id;
        if (dataObjectId is not null && _dataObjectInstanceRegistry.IsRegistered(dataObjectId))
        {
          ResolveDataObjectInstanceProxies(atomicContext);
        }
      }
    }
  }

  /// <summary>
  /// Converts and caches an atomic object for later lookup.
  /// Skips registered DataObjects (displayValue is InstanceProxy) - they are resolved in ResolveDataObjectInstanceProxies.
  /// </summary>
  private void ConvertObjectToCache(TraversalContext atomicContext)
  {
    var obj = atomicContext.Current;
    var objId = obj.applicationId ?? obj.id;

    if (objId is null || ConvertedObjectsMap.ContainsKey(objId))
    {
      return;
    }

    // skip registered DataObjects - they'll be processed in second pass
    if (obj is DataObject dataObject)
    {
      var id = dataObject.applicationId ?? dataObject.id.NotNull();
      if (_dataObjectInstanceRegistry.IsRegistered(id))
      {
        return;
      }
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

      // handle all DataObjects
      if (obj is DataObject normalDataObject)
      {
        var geometries = ConvertToGeometryWrappers(converted);
        var dataObjectWrapper = CreateDataObjectWrapper(normalDataObject, geometries, path, objectCollection);

        CollectionRebuilder.AppendSpeckleGrasshopperObject(dataObjectWrapper, path, _colorUnpacker, _materialUnpacker);
        return;
      }

      // nothing converted - nothing to do (for non-DataObjects)
      if (converted.Count == 0)
      {
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
      // don't throw - continue processing other objects
      _logger.LogError(ex, "Failed to convert object {objectId} of type {objectType}", objId, obj.speckle_type);
    }
  }

  /// <summary>
  /// Resolves a registered DataObject by transforming its InstanceProxy definition objects.
  /// Requires definition objects to exist in ConvertedObjectsMap (populated by ConvertObjectToCache).
  /// </summary>
  private void ResolveDataObjectInstanceProxies(TraversalContext atomicContext)
  {
    var obj = atomicContext.Current;
    if (obj is not DataObject dataObject)
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

      var primitiveConverted = dataObject
        .displayValue.Where(item => item is not InstanceProxy)
        .SelectMany(item => SpeckleConversionContext.Current.ConvertToHost(item))
        .ToList();

      resolvedGeometries.AddRange(ConvertToGeometryWrappers(primitiveConverted));

      var dataObjectWrapper = CreateDataObjectWrapper(dataObject, resolvedGeometries, path, objectCollection);

      CollectionRebuilder.AppendSpeckleGrasshopperObject(dataObjectWrapper, path, _colorUnpacker, _materialUnpacker);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // don't throw - continue processing other DataObjects
      _logger.LogError(ex, "Failed to resolve DataObject {dataObjectId} with InstanceProxies", dataObjectId);
    }
  }

  /// <summary>
  /// Converts block instances and definitions from traversal contexts into Grasshopper wrapper objects.
  /// Automatically filters out InstanceProxies belonging to registered DataObjects.
  /// Automatically handles cleanup of consumed objects from the collection hierarchy.
  /// </summary>
  public void ConvertBlockInstances(IReadOnlyCollection<TraversalContext> blockInstances)
  {
    // build set of registered InstanceProxy IDs for fast lookup
    var registeredProxyIds = new HashSet<string>();
    foreach (var entry in _dataObjectInstanceRegistry.GetEntries().Values)
    {
      foreach (var proxy in entry.InstanceProxies)
      {
        var proxyId = proxy.applicationId ?? proxy.id;
        if (proxyId is not null)
        {
          registeredProxyIds.Add(proxyId);
        }
      }
    }

    // filter out InstanceProxies that belong to registered DataObjects
    var filteredBlockInstances = blockInstances
      .Where(tc =>
      {
        if (tc.Current is InstanceProxy proxy)
        {
          var proxyId = proxy.applicationId ?? proxy.id;
          return proxyId is null || !registeredProxyIds.Contains(proxyId);
        }
        return true;
      })
      .ToList();

    var blockUnpacker = new GrasshopperBlockUnpacker(_traversalContextUnpacker, _colorUnpacker, _materialUnpacker);

    // get consumed object IDs from unpacker
    var consumedObjectIds = blockUnpacker.UnpackBlocks(
      filteredBlockInstances,
      _definitionProxies,
      ConvertedObjectsMap,
      CollectionRebuilder
    );

    // clean up consumed objects from collections
    CollectionRebuilder.RemoveConsumedObjects(consumedObjectIds);
  }

  /// <summary>
  /// Creates a DataObjectWrapper from a DataObject and its geometries.
  /// Handles color/material inheritance and property extraction.
  /// </summary>
  private SpeckleDataObjectWrapper CreateDataObjectWrapper(
    DataObject dataObject,
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
    if (_definitionProxies is not null)
    {
      foreach (var defProxy in _definitionProxies)
      {
        var defId = defProxy.applicationId ?? defProxy.id;
        if (defId is not null)
        {
          definitionObjectsMap[defId] = defProxy.objects;
        }
      }
    }

    foreach (var instanceProxy in instanceProxies)
    {
      // get the definition objects for this instance
      if (!definitionObjectsMap.TryGetValue(instanceProxy.definitionId, out var definitionObjectIds))
      {
        continue; // definition not found, skip this proxy
      }

      // get transform from the instance proxy
      var transform = GrasshopperHelpers.MatrixToTransform(instanceProxy.transform, instanceProxy.units);

      // apply transform to each definition object
      foreach (var objectId in definitionObjectIds)
      {
        if (ConvertedObjectsMap.TryGetValue(objectId, out var definitionObject))
        {
          // deep copy and transform the geometry
          var transformedWrapper = definitionObject.DeepCopy();

          // transform the GeometryBase
          transformedWrapper.GeometryBase.NotNull().Transform(transform);

          // keep Base and GeometryBase in sync (CNX-2847)
          // Exception shouldn't ever happen for objects in ConvertedObjectsMap
          transformedWrapper.Base =
            SpeckleConversionContext.Current.ConvertToSpeckle(transformedWrapper.GeometryBase)
            ?? throw new InvalidOperationException($"Failed to convert transformed geometry for object {objectId}");

          // preserve metadata from original Base
          transformedWrapper.Base.applicationId = definitionObject.Base.applicationId;
          transformedWrapper.Base["units"] = _settingsStore.Current.SpeckleUnits;

          resolvedGeometries.Add(transformedWrapper);
        }
      }
    }

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
