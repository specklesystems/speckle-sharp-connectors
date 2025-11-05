using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// <para>Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public class RhinoHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly RhinoInstanceBaker _instanceBaker;
  private readonly RhinoLayerBaker _layerBaker;
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoColorBaker _colorBaker;
  private readonly RhinoGroupBaker _groupBaker;
  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly IThreadContext _threadContext;
  private readonly IReceiveConversionHandler _conversionHandler;

  public RhinoHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    RhinoLayerBaker layerBaker,
    RootObjectUnpacker rootObjectUnpacker,
    RhinoInstanceBaker instanceBaker,
    RhinoMaterialBaker materialBaker,
    RhinoColorBaker colorBaker,
    RhinoGroupBaker groupBaker,
    ISdkActivityFactory activityFactory,
    IThreadContext threadContext,
    IReceiveConversionHandler conversionHandler
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _rootObjectUnpacker = rootObjectUnpacker;
    _instanceBaker = instanceBaker;
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
    _layerBaker = layerBaker;
    _groupBaker = groupBaker;
    _activityFactory = activityFactory;
    _threadContext = threadContext;
    _conversionHandler = conversionHandler;
  }

#pragma warning disable CA1506, CA1502
  public Task<HostObjectBuilderResult> Build(
#pragma warning restore CA1506, CA1502
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");
    // POC: This is where the top level base-layer name is set. Could be abstracted or injected in the context?
    var baseLayerName = $"Project {projectName}: Model {modelName}";

    // 0 - Clean then Rock n Roll!
    PreReceiveDeepClean(baseLayerName);

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);

    // 2 - Split atomic objects and instance components with their path
    var (
      atomicObjectsWithoutInstanceComponents,
      atomicInstanceComponents,
      atomicObjectsWithInstanceComponents,
      displayInstanceComponents
    ) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(unpackedRoot.ObjectsToConvert);

    var atomicObjectsWithoutInstanceComponentsWithPath = _layerBaker.GetAtomicObjectsWithPath(
      atomicObjectsWithoutInstanceComponents
    );
    var atomicInstanceComponentsWithPath = _layerBaker.GetInstanceComponentsWithPath(atomicInstanceComponents);
    var atomicObjectsWithInstanceComponentsWithPath = _layerBaker.GetAtomicObjectsWithPath(
      atomicObjectsWithInstanceComponents
    );
    var displayInstanceComponentsWithPath = _layerBaker.GetInstanceComponentsWithPath(displayInstanceComponents);

    // 2.0 - POC!! this could be done with a traversal helper!!
    // create a map between atomic objects with display instances, and the display instances of that atomic object (index)
    Dictionary<int, List<int>> displayInstanceIdMap = new();
    for (int i = 0; i < displayInstanceComponents.Count; i++)
    {
      TraversalContext displayInstanceComponent = displayInstanceComponents.ElementAt(i);
      for (int j = 0; j < atomicObjectsWithInstanceComponents.Count; j++)
      {
        if (displayInstanceComponent.Parent == atomicObjectsWithInstanceComponents.ElementAt(j))
        {
          if (displayInstanceIdMap.TryGetValue(j, out List<int>? value))
          {
            value.Add(i);
          }
          else
          {
            displayInstanceIdMap.Add(j, new List<int>() { i });
          }
        }
      }
    }

    // 2.1 - these are not captured by traversal, so we need to re-add them here
    if (unpackedRoot.DefinitionProxies != null && unpackedRoot.DefinitionProxies.Count > 0)
    {
      var transformed = unpackedRoot.DefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      // POC:!!!! commenting this out for now, because we have no way of differentiating between atomic instance definitions and display instance definitions.
      // This means that currently atomic instances are broken.
      // we should introduce a separate root key to store display value definitions.
      //atomicInstanceComponentsWithPath.AddRange(transformed);
      displayInstanceComponentsWithPath.AddRange(transformed);
    }

    // 3 - Bake materials and colors, as they are used later down the line by layers and objects
    onOperationProgressed.Report(new("Converting materials and colors", null));
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      using var _ = _activityFactory.Start("Render Materials");
      _threadContext.RunOnMain(() =>
      {
        _materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies, baseLayerName);
      });
    }

    if (unpackedRoot.ColorProxies != null)
    {
      _colorBaker.ParseColors(unpackedRoot.ColorProxies);
    }

    // 4 - Bake layers
    // See [CNX-325: Rhino: Change receive operation order to increase performance](https://linear.app/speckle/issue/CNX-325/rhino-change-receive-operation-order-to-increase-performance)
    onOperationProgressed.Report(new("Baking layers (redraw disabled)", null));
    using (var _ = _activityFactory.Start("Pre baking layers"))
    {
      //Rhino 8 doesn't play nice with Eto and layers
      _threadContext
        .RunOnMain(() =>
        {
          using var layerNoDraw = new DisableRedrawScope(_converterSettings.Current.Document.Views);
          var paths = atomicObjectsWithoutInstanceComponentsWithPath.Select(t => t.path).ToList();
          paths.AddRange(atomicObjectsWithInstanceComponentsWithPath.Select(t => t.path));
          paths.AddRange(atomicInstanceComponentsWithPath.Select(t => t.path));
          _layerBaker.CreateAllLayersForReceive(paths, baseLayerName);
        })
        .Wait(cancellationToken);
    }

    // 5 - Convert atomic objects without instances first!!
    var bakedObjectIds = new HashSet<string>();
    Dictionary<string, IReadOnlyCollection<string>> applicationIdMap = new(); // This map is used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking
    HashSet<ReceiveConversionResult> conversionResults = new();

    int count = 0;
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      foreach (var (path, obj) in atomicObjectsWithoutInstanceComponentsWithPath)
      {
        onOperationProgressed.Report(
          new("Converting objects", (double)++count / atomicObjectsWithoutInstanceComponents.Count)
        );
        var ex = _conversionHandler.TryConvert(() =>
        {
          // 0: get pre-created layer from cache in layer baker
          int layerIndex = _layerBaker.GetLayerIndex(path, baseLayerName);
          cancellationToken.ThrowIfCancellationRequested();

          // 1: create object attributes for baking
          ObjectAttributes atts = obj.GetAttributes();
          atts.LayerIndex = layerIndex;

          // 2: convert
          var result = _converter.Convert(obj);

          // 3: bake
          var conversionIds = new List<string>();
          if (result is GeometryBase geometryBase)
          {
            var guid = BakeObject(geometryBase, obj, null, atts);
            conversionIds.Add(guid.ToString());
          }
          else if (result is List<GeometryBase> geometryBases) // one to many raw encoding case
          {
            // NOTE: I'm unhappy about this case (dim). It's needed as the raw encoder approach can hypothetically return
            // multiple "geometry bases" - but this is not a fallback conversion.
            // EXTRA NOTE: Oguzhan says i shouldn't be unhappy about this - it's a legitimate case
            // EXTRA EXTRA NOTE: TY Ogu, i am no longer than unhappy about it. It's legit "mess".
            foreach (var gb in geometryBases)
            {
              var guid = BakeObject(gb, obj, null, atts);
              conversionIds.Add(guid.ToString());
            }
          }
          else if (result is List<(GeometryBase, Base)> fallbackConversionResult) // one to many fallback conversion
          {
            var guids = BakeObjectsAsFallbackGroup(fallbackConversionResult, new(), obj, atts, baseLayerName);
            conversionIds.AddRange(guids.Select(id => id.ToString()));
          }

          if (conversionIds.Count == 0)
          {
            // TODO: add this condition to report object - same as in autocad
            throw new ConversionException("Object did not convert to any native geometry");
          }

          // 4: log
          var id = conversionIds[0]; // this is group id if it is a one to many conversion, otherwise id of object itself
          conversionResults.Add(new(Status.SUCCESS, obj, id, result.GetType().ToString()));
          if (conversionIds.Count == 1)
          {
            bakedObjectIds.Add(id);
          }
          else
          {
            // first item always a group id if it is a one-to-many,
            // we do not want to deal with later groups and its sub elements. It causes a huge issue on performance.
            bakedObjectIds.AddRange(conversionIds.Skip(1));
          }

          // 5: populate app id map
          applicationIdMap[obj.applicationId ?? obj.id.NotNull()] = conversionIds;
        });
        if (ex is not null)
        {
          conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
        }
      }
    }

    // 6 - Convert instances
    IReadOnlyCollection<string> createdDisplayIds;
    using (var _ = _activityFactory.Start("Converting instances"))
    {
      // bake atomic instances
      var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = _instanceBaker.BakeInstances(
        atomicInstanceComponentsWithPath,
        applicationIdMap,
        baseLayerName,
        onOperationProgressed
      );

      bakedObjectIds.RemoveWhere(id => consumedObjectIds.Contains(id)); // remove all objects that have been "consumed"
      bakedObjectIds.UnionWith(createdInstanceIds); // add instance ids
      conversionResults.RemoveWhere(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId)); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
      conversionResults.UnionWith(instanceConversionResults); // add instance conversion results to our list

      // bake display instances
      var (createdDisplayInstanceIds, consumedDisplayObjectIds, displayInstanceConversionResults) =
        _instanceBaker.BakeInstances(
          displayInstanceComponentsWithPath,
          applicationIdMap,
          baseLayerName,
          onOperationProgressed
        );

      createdDisplayIds = createdDisplayInstanceIds;
      conversionResults.RemoveWhere(result =>
        result.ResultId != null && consumedDisplayObjectIds.Contains(result.ResultId)
      ); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
    }

    // 7 - Convert atomic objects with instance components
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      for (int i = 0; i < atomicObjectsWithInstanceComponentsWithPath.Count; i++)
      {
        var (path, obj) = atomicObjectsWithInstanceComponentsWithPath.ElementAt(i);
        onOperationProgressed.Report(
          new("Converting objects", (double)++count / atomicObjectsWithInstanceComponentsWithPath.Count)
        );
        var ex = _conversionHandler.TryConvert(() =>
        {
          // 0: get pre-created layer from cache in layer baker
          int layerIndex = _layerBaker.GetLayerIndex(path, baseLayerName);
          cancellationToken.ThrowIfCancellationRequested();

          // 1: create object attributes for baking
          ObjectAttributes atts = obj.GetAttributes();
          atts.LayerIndex = layerIndex;

          // 2: convert
          var result = _converter.Convert(obj);

          // 3: bake
          var conversionIds = new List<string>();

          if (result is List<(GeometryBase, Base)> fallbackConversionResult) // one to many fallback conversion, this should be the only type of non instance atomic object with instances in display value
          {
            // add display instances here
            List<string> createdInstanceIds = new();
            if (displayInstanceIdMap.TryGetValue(i, out List<int>? value))
            {
              foreach (var instanceIndex in value)
              {
                createdInstanceIds.Add(createdDisplayIds.ElementAt(instanceIndex));
              }
            }
            var guids = BakeObjectsAsFallbackGroup(
              fallbackConversionResult,
              createdInstanceIds,
              obj,
              atts,
              baseLayerName
            );
            conversionIds.AddRange(guids.Select(id => id.ToString()));
          }

          if (conversionIds.Count == 0)
          {
            // TODO: add this condition to report object - same as in autocad
            throw new ConversionException("Object did not convert to any native geometry");
          }

          // 4: log
          var id = conversionIds[0]; // this is group id if it is a one to many conversion, otherwise id of object itself
          conversionResults.Add(new(Status.SUCCESS, obj, id, result.GetType().ToString()));
          if (conversionIds.Count == 1)
          {
            bakedObjectIds.Add(id);
          }
          else
          {
            // first item always a group id if it is a one-to-many,
            // we do not want to deal with later groups and its sub elements. It causes a huge issue on performance.
            bakedObjectIds.AddRange(conversionIds.Skip(1));
          }

          // 5: populate app id map
          applicationIdMap[obj.applicationId ?? obj.id.NotNull()] = conversionIds;
        });
        if (ex is not null)
        {
          conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
        }
      }
    }

    // 7 - Create groups
    if (unpackedRoot.GroupProxies is not null)
    {
      _groupBaker.BakeGroups(unpackedRoot.GroupProxies, applicationIdMap, baseLayerName);
    }

    _converterSettings.Current.Document.Views.Redraw();
    return Task.FromResult(new HostObjectBuilderResult(bakedObjectIds, conversionResults));
  }

  private void PreReceiveDeepClean(string baseLayerName)
  {
    // Remove all previously received layers and render materials from the document
    int rootLayerIndex = _converterSettings.Current.Document.Layers.Find(
      Guid.Empty,
      baseLayerName,
      RhinoMath.UnsetIntIndex
    );

    //Rhino 8 doesn't play nice with Eto and layers
    _threadContext
      .RunOnMain(() =>
      {
        _instanceBaker.PurgeInstances(baseLayerName);
        _materialBaker.PurgeMaterials(baseLayerName);

        var doc = _converterSettings.Current.Document;
        // Cleans up any previously received objects
        if (rootLayerIndex != RhinoMath.UnsetIntIndex)
        {
          var documentLayer = doc.Layers[rootLayerIndex];
          var childLayers = documentLayer.GetChildren();
          if (childLayers != null)
          {
            using var layerNoDraw = new DisableRedrawScope(doc.Views);
            foreach (var layer in childLayers)
            {
              var purgeSuccess = doc.Layers.Purge(layer.Index, true);
              if (!purgeSuccess)
              {
                Console.WriteLine($"Failed to purge layer: {layer}");
              }
            }
          }
          doc.Layers.Purge(documentLayer.Index, true);
        }

        // Cleans up any previously received group
        _groupBaker.PurgeGroups(baseLayerName);
      })
      .Wait();
  }

  /// <summary>
  /// Bakes an object to the document.
  /// </summary>
  /// <param name="obj"></param>
  /// <param name="originalObject"></param>
  /// <param name="parentObjectId">Parent object ID for color and material proxies search (if fallback conversion was used)</param>
  /// <param name="atts"></param>
  /// <returns></returns>
  /// <remarks>
  /// Material and Color attributes are processed here due to those properties existing sometimes on fallback geometry (instead of parent).
  /// and this method is called by <see cref="BakeObjectsAsFallbackGroup"/>
  /// </remarks>
  private Guid BakeObject(GeometryBase obj, Base originalObject, string? parentObjectId, ObjectAttributes atts)
  {
    var objectId = originalObject.applicationId ?? originalObject.id.NotNull();

    if (_materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(objectId, out int mIndex))
    {
      atts.MaterialIndex = mIndex;
      atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
    }
    else if (
      parentObjectId is not null
      && (_materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(parentObjectId, out int mIndexSpeckleObj))
    )
    {
      atts.MaterialIndex = mIndexSpeckleObj;
      atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
    }

    if (_colorBaker.ObjectColorsIdMap.TryGetValue(objectId, out (Color, ObjectColorSource) color))
    {
      atts.ObjectColor = color.Item1;
      atts.ColorSource = color.Item2;
    }
    else if (
      parentObjectId is not null
      && (_colorBaker.ObjectColorsIdMap.TryGetValue(parentObjectId, out (Color, ObjectColorSource) colorSpeckleObj))
    )
    {
      atts.ObjectColor = colorSpeckleObj.Item1;
      atts.ColorSource = colorSpeckleObj.Item2;
    }

    return _converterSettings.Current.Document.Objects.Add(obj, atts);
  }

  private List<Guid> BakeObjectsAsFallbackGroup(
    IEnumerable<(GeometryBase, Base)> fallbackConversionResult,
    List<string> createdInstanceIds,
    Base originatingObject,
    ObjectAttributes atts,
    string baseLayerName
  )
  {
    List<Guid> objectIds = new();
    string parentId = originatingObject.applicationId ?? originatingObject.id.NotNull();
    int objCount = 0;
    foreach (var (conversionResult, originalBaseObject) in fallbackConversionResult)
    {
      var id = BakeObject(conversionResult, originalBaseObject, parentId, atts);
      objectIds.Add(id);
      objCount++;
    }

    // now add already created instances
    foreach (string instanceId in createdInstanceIds)
    {
      var instanceGuid = new Guid(instanceId);
      var docObject = _converterSettings.Current.Document.Objects.FindId(instanceGuid);
      if (docObject is null)
      {
        continue;
      }
      docObject.Attributes = atts;
      docObject.CommitChanges();
      objCount++;
      objectIds.Add(instanceGuid);
    }

    // only create groups if we really need to, ie if the fallback conversion result count is bigger than one.
    if (objCount > 1)
    {
      var groupIndex = _converterSettings.Current.Document.Groups.Add(
        $@"{originatingObject.speckle_type.Split('.').Last()} - {parentId}  ({baseLayerName})",
        objectIds
      );

      var group = _converterSettings.Current.Document.Groups.FindIndex(groupIndex);

      objectIds.Insert(0, group.Id);
    }

    return objectIds;
  }
}
