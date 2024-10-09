using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
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

  public RhinoHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    RhinoLayerBaker layerBaker,
    RootObjectUnpacker rootObjectUnpacker,
    RhinoInstanceBaker instanceBaker,
    RhinoMaterialBaker materialBaker,
    RhinoColorBaker colorBaker,
    RhinoGroupBaker groupBaker,
    ISdkActivityFactory activityFactory
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
  }

  public async Task<HostObjectBuilderResult> Build(
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
    _layerBaker.CreateBaseLayer(baseLayerName);

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );
    var atomicObjectsWithPath = _layerBaker.GetAtomicObjectsWithPath(atomicObjects);
    var instanceComponentsWithPath = _layerBaker.GetInstanceComponentsWithPath(instanceComponents);

    // 2.1 - these are not captured by traversal, so we need to re-add them here
    if (unpackedRoot.DefinitionProxies != null && unpackedRoot.DefinitionProxies.Count > 0)
    {
      var transformed = unpackedRoot.DefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      instanceComponentsWithPath.AddRange(transformed);
    }

    // 3 - Bake materials and colors, as they are used later down the line by layers and objects
    onOperationProgressed.Report(new("Converting materials and colors", null));
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      using var _ = _activityFactory.Start("Render Materials");
      _materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies, baseLayerName);
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
      using var layerNoDraw = new DisableRedrawScope(_converterSettings.Current.Document.Views);
      foreach (var (path, _) in atomicObjectsWithPath)
      {
        _layerBaker.GetAndCreateLayerFromPath(path, baseLayerName);
      }
    }

    // 5 - Convert atomic objects
    List<string> bakedObjectIds = new();
    Dictionary<string, List<string>> applicationIdMap = new(); // This map is used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking
    List<ReceiveConversionResult> conversionResults = new();

    int count = 0;
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      foreach (var (path, obj) in atomicObjectsWithPath)
      {
        using (var convertActivity = _activityFactory.Start("Converting object"))
        {
          onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));
          try
          {
            // 1: get pre-created layer from cache in layer baker
            int layerIndex = _layerBaker.GetAndCreateLayerFromPath(path, baseLayerName);

            // 2: convert
            var result = _converter.Convert(obj);

            // 3: bake
            var conversionIds = new List<string>();
            if (result is GeometryBase geometryBase)
            {
              var guid = BakeObject(geometryBase, obj, layerIndex);
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
                var guid = BakeObject(gb, obj, layerIndex);
                conversionIds.Add(guid.ToString());
              }
            }
            else if (result is IEnumerable<(object, Base)> fallbackConversionResult) // one to many fallback conversion
            {
              var guids = BakeObjectsAsGroup(fallbackConversionResult, obj, layerIndex, baseLayerName);
              conversionIds.AddRange(guids.Select(id => id.ToString()));
            }

            if (conversionIds.Count == 0)
            {
              throw new SpeckleConversionException($"Failed to convert object.");
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
            applicationIdMap[obj.applicationId ?? obj.id] = conversionIds;
            convertActivity?.SetStatus(SdkActivityStatusCode.Ok);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            conversionResults.Add(new(Status.ERROR, obj, null, null, ex));
            convertActivity?.SetStatus(SdkActivityStatusCode.Error);
            convertActivity?.RecordException(ex);
          }
        }
      }
    }

    // 6 - Convert instances
    using (var _ = _activityFactory.Start("Converting instances"))
    {
      var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = await _instanceBaker
        .BakeInstances(instanceComponentsWithPath, applicationIdMap, baseLayerName, onOperationProgressed)
        .ConfigureAwait(false);

      bakedObjectIds.RemoveAll(id => consumedObjectIds.Contains(id)); // remove all objects that have been "consumed"
      bakedObjectIds.AddRange(createdInstanceIds); // add instance ids
      conversionResults.RemoveAll(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId)); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
      conversionResults.AddRange(instanceConversionResults); // add instance conversion results to our list
    }

    // 7 - Create groups
    if (unpackedRoot.GroupProxies is not null)
    {
      _groupBaker.BakeGroups(unpackedRoot.GroupProxies, applicationIdMap, baseLayerName);
    }

    _converterSettings.Current.Document.Views.Redraw();

    return new HostObjectBuilderResult(bakedObjectIds, conversionResults);
  }

  private void PreReceiveDeepClean(string baseLayerName)
  {
    // Remove all previously received layers and render materials from the document
    int rootLayerIndex = _converterSettings.Current.Document.Layers.Find(
      Guid.Empty,
      baseLayerName,
      RhinoMath.UnsetIntIndex
    );

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
    }

    // Cleans up any previously received group
    _groupBaker.PurgeGroups(baseLayerName);
  }

  private Guid BakeObject(GeometryBase obj, Base originalObject, int layerIndex)
  {
    ObjectAttributes atts = new() { LayerIndex = layerIndex };
    var objectId = originalObject.applicationId ?? originalObject.id;

    if (_materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(objectId, out int mIndex))
    {
      atts.MaterialIndex = mIndex;
      atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
    }

    if (_colorBaker.ObjectColorsIdMap.TryGetValue(objectId, out (Color, ObjectColorSource) color))
    {
      atts.ObjectColor = color.Item1;
      atts.ColorSource = color.Item2;
    }

    return _converterSettings.Current.Document.Objects.Add(obj, atts);
  }

  private List<Guid> BakeObjectsAsGroup(
    IEnumerable<(object, Base)> fallbackConversionResult,
    Base originatingObject,
    int layerIndex,
    string baseLayerName
  )
  {
    List<Guid> objectIds = new();
    foreach (var (conversionResult, originalBaseObject) in fallbackConversionResult)
    {
      if (conversionResult is not GeometryBase geometryBase)
      {
        // TODO: throw?
        continue;
      }

      var id = BakeObject(geometryBase, originalBaseObject, layerIndex);
      objectIds.Add(id);
    }

    var groupIndex = _converterSettings.Current.Document.Groups.Add(
      $@"{originatingObject.speckle_type.Split('.').Last()} - {originatingObject.applicationId ?? originatingObject.id}  ({baseLayerName})",
      objectIds
    );
    var group = _converterSettings.Current.Document.Groups.FindIndex(groupIndex);
    objectIds.Insert(0, group.Id);
    return objectIds;
  }
}
