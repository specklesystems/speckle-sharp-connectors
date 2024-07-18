using System.Diagnostics.Contracts;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Internal.Mapping.CommonControls.Transformations;
using ArcGIS.Desktop.Mapping;
using Objects.Geometry;
using Objects.GIS;
using Speckle.Connectors.ArcGIS.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.Extensions;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Models.Instances;
using Speckle.DoubleNumerics;
using RasterLayer = Objects.GIS.RasterLayer;

namespace Speckle.Connectors.ArcGIS.Operations.Receive;

public record LocalToGlobalMap(Base AtomicObject, TraversalContext tc, List<Matrix4x4> Matrix);

public class LocalToGlobal
{
  private static Vector3 TransformPt(Vector3 vector, Matrix4x4 matrix)
  {
    var divisor = matrix.M41 + matrix.M42 + matrix.M43 + matrix.M44;
    var x = (vector.X * matrix.M11 + vector.Y * matrix.M12 + vector.Z * matrix.M13 + matrix.M14) / divisor;
    var y = (vector.X * matrix.M21 + vector.Y * matrix.M22 + vector.Z * matrix.M23 + matrix.M24) / divisor;
    var z = (vector.X * matrix.M31 + vector.Y * matrix.M32 + vector.Z * matrix.M33 + matrix.M34) / divisor;

    return new Vector3(x, y, z);
  }

  public static Base TransformObjects(Base atomicObject, TraversalContext ctx, List<Matrix4x4> matrix)
  {
    if (matrix.Count == 0)
    {
      return atomicObject;
    }

    Base newObject = atomicObject.ShallowCopy();
    List<Mesh> newDisplayValue = new();

    var displayValue = newObject.TryGetDisplayValue();
    if (displayValue is null)
    {
      throw new SpeckleException($"{newObject.speckle_type} blocks contains no display value");
    }

    foreach (Base displayVal in displayValue)
    {
      if (displayVal is Mesh displayMesh)
      {
        List<double> vertices = new();
        for (int i = 0; i < displayMesh.vertices.Count; i++)
        {
          if (i % 3 != 0)
          {
            continue;
          }
          var ptVector = new Vector3(
            displayMesh.vertices[i],
            displayMesh.vertices[i + 1],
            displayMesh.vertices[i + 2]
          //1
          );

          foreach (var matr in matrix)
          {
            ptVector = TransformPt(ptVector, matr); //Vector3.Dot(ptVector, matr);
          }
          vertices.AddRange([ptVector.X, ptVector.Y, ptVector.Z]);
        }
        Mesh newMesh = new();
        newMesh.vertices = vertices;
        newMesh.faces = new List<int>(displayMesh.faces);
        newMesh.colors = new List<int>(displayMesh.colors);
        newDisplayValue.Add(newMesh);
      }
      else
      {
        throw new SpeckleException($"Blocks containing {atomicObject.speckle_type} are not supported");
      }
    }

    newObject["displayValue"] = newDisplayValue;
    return newObject;
  }

  public List<LocalToGlobalMap> LocalToGlobalMaps { get; } = new();

  private static string[] GetLayerPath(TraversalContext context)
  {
    string[] collectionBasedPath = context.GetAscendantOfType<Collection>().Select(c => c.name).ToArray();
    string[] reverseOrderPath =
      collectionBasedPath.Length != 0 ? collectionBasedPath : context.GetPropertyPath().ToArray();

    var originalPath = reverseOrderPath.Reverse().ToArray();
    return originalPath.Where(x => !string.IsNullOrEmpty(x)).ToArray();
  }

  public List<LocalToGlobalMap> UnpackRelativeAtomicObjects(Base rootObject, List<TraversalContext> objectsToConvert)
  {
    var instanceDefinitionProxies = (rootObject["instanceDefinitionProxies"] as List<object>)
      ?.Cast<InstanceDefinitionProxy>()
      .ToList();

    var instanceComponents = new List<(TraversalContext layerPath, InstanceProxy obj)>();
    var atomicObjects = new List<(TraversalContext layerPath, Base obj)>();

    // Split up the instances from the non-instances
    foreach (TraversalContext tc in objectsToConvert)
    {
      var path = GetLayerPath(tc);
      if (tc.Current is InstanceProxy instanceComponent)
      {
        instanceComponents.Add((tc, instanceComponent));
      }
      else
      {
        atomicObjects.Add((tc, tc.Current));
      }
    }

    var objectsAtAbsolute = new List<(TraversalContext layerPath, Base obj)>();
    var objectsAtRelative = new List<(TraversalContext layerPath, Base obj)>();

    foreach ((TraversalContext layerPath, Base obj) in atomicObjects)
    {
      if (obj.applicationId is null)
      {
        continue;
      }
      if (
        instanceDefinitionProxies is not null
        && instanceDefinitionProxies.Any(idp => idp.objects.Contains(obj.applicationId))
      )
      {
        objectsAtRelative.Add((layerPath, obj)); // to use in Instances only
      }
      else
      {
        objectsAtAbsolute.Add((layerPath, obj)); // to bake
      }
    }
    // objectsAtRelative.AddRange(instanceComponents.Select(x => (x.layerPath, (Base)(x.obj))).ToList());

    foreach ((TraversalContext tc, Base obj) in objectsAtAbsolute)
    {
      LocalToGlobalMaps.Add(new LocalToGlobalMap(obj, tc, new List<Matrix4x4>()));
    }

    if (instanceDefinitionProxies is null)
    {
      return LocalToGlobalMaps;
    }

    void UnpackMatrix(Base objectToUnpack, TraversalContext layerPath, List<Matrix4x4> matrices)
    {
      if (objectToUnpack.applicationId is null)
      {
        return;
      }
      InstanceDefinitionProxy? definitionProxy = instanceDefinitionProxies.Find(idp =>
        idp.objects.Contains(objectToUnpack.applicationId)
      );
      if (definitionProxy is null)
      {
        return;
      }
      var instances = instanceComponents.Where(ic => ic.obj.definitionId == definitionProxy.applicationId);
      foreach ((TraversalContext tc, InstanceProxy instance) in instances)
      {
        matrices.Add(instance.transform);
        UnpackMatrix(instance, tc, matrices);
        LocalToGlobalMaps.Add(new LocalToGlobalMap(objectToUnpack, tc, matrices));
        matrices = new List<Matrix4x4>();
      }
      LocalToGlobalMaps.Add(new LocalToGlobalMap(objectToUnpack, layerPath, matrices));
    }

    foreach ((TraversalContext layerPath, Base objectAtRelative) in objectsAtRelative)
    {
      UnpackMatrix(objectAtRelative, layerPath, new List<Matrix4x4>());
    }

    return LocalToGlobalMaps.Where(ltgm => ltgm.AtomicObject is not InstanceProxy).ToList();
  }
}

public class ArcGISHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly INonNativeFeaturesUtils _nonGisFeaturesUtils;

  // POC: figure out the correct scope to only initialize on Receive
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;
  private readonly GraphTraversal _traverseFunction;

  public ArcGISHostObjectBuilder(
    IRootToHostConverter converter,
    IConversionContextStack<ArcGISDocument, Unit> contextStack,
    INonNativeFeaturesUtils nonGisFeaturesUtils,
    GraphTraversal traverseFunction
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _nonGisFeaturesUtils = nonGisFeaturesUtils;
    _traverseFunction = traverseFunction;
  }

  public HostObjectBuilderResult Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // TODO get spatialRef and offsets & rotation from ProjectInfo in CommitObject
    // ATM, GIS commit CRS is stored per layer (in FeatureClass converter), but should be moved to the Root level too

    // Prompt the UI conversion started. Progress bar will swoosh.
    onOperationProgressed?.Invoke("Converting", null);

    var objectsToConvertTc = _traverseFunction
      .Traverse(rootObject)
      .Where(ctx => ctx.Current is not Collection || IsGISType(ctx.Current))
      .Where(ctx => HasGISParent(ctx) is false)
      .ToList();

    var localToGlobal = new LocalToGlobal();
    var localToGlobalMap = localToGlobal.UnpackRelativeAtomicObjects(rootObject, objectsToConvertTc);
    var objectsToConvert = localToGlobalMap.Select(x => (x.AtomicObject, x.tc, x.Matrix)).ToList();

    int allCount = objectsToConvert.Count;
    int count = 0;
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker = new();

    // 1. convert everything
    List<ReceiveConversionResult> results = new(objectsToConvert.Count);
    List<string> bakedObjectIds = new();
    foreach ((Base atomicObject, TraversalContext ctx, List<Matrix4x4> matrix) in objectsToConvert)
    {
      string[] path = GetLayerPath(ctx);
      Base obj = ctx.Current;

      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        if (IsGISType(obj))
        {
          string nestedLayerPath = $"{string.Join("\\", path)}";
          string datasetId = (string)_converter.Convert(obj);
          conversionTracker[ctx] = new ObjectConversionTracker(obj, nestedLayerPath, datasetId);
        }
        else
        {
          obj = LocalToGlobal.TransformObjects(atomicObject, ctx, matrix);

          string nestedLayerPath = $"{string.Join("\\", path)}\\{obj.speckle_type.Split(".")[^1]}";
          Geometry converted = (Geometry)_converter.Convert(obj);
          conversionTracker[ctx] = new ObjectConversionTracker(obj, nestedLayerPath, converted);
        }
      }
      catch (Exception ex) when (!ex.IsFatal()) // DO NOT CATCH SPECIFIC STUFF, conversion errors should be recoverable
      {
        results.Add(new(Status.ERROR, obj, null, null, ex));
      }
      onOperationProgressed?.Invoke("Converting", (double)++count / allCount);
    }

    // 2. convert Database entries with non-GIS geometry datasets
    onOperationProgressed?.Invoke("Writing to Database", null);
    _nonGisFeaturesUtils.WriteGeometriesToDatasets(conversionTracker);

    // Create main group layer
    Dictionary<string, GroupLayer> createdLayerGroups = new();
    Map map = _contextStack.Current.Document.Map;
    GroupLayer groupLayer = LayerFactory.Instance.CreateGroupLayer(map, 0, $"{projectName}: {modelName}");
    createdLayerGroups["Basic Speckle Group"] = groupLayer; // key doesn't really matter here

    // 3. add layer and tables to the Table Of Content
    int bakeCount = 0;
    Dictionary<string, MapMember> bakedMapMembers = new();
    onOperationProgressed?.Invoke("Adding to Map", bakeCount);

    foreach (var item in conversionTracker)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var trackerItem = conversionTracker[item.Key]; // updated tracker object

      // BAKE OBJECTS HERE
      if (trackerItem.Exception != null)
      {
        results.Add(new(Status.ERROR, trackerItem.Base, null, null, trackerItem.Exception));
      }
      else if (trackerItem.DatasetId == null)
      {
        results.Add(
          new(
            Status.ERROR,
            trackerItem.Base,
            null,
            null,
            new ArgumentException($"Unknown error: Dataset not created for {trackerItem.Base.speckle_type}")
          )
        );
      }
      else if (bakedMapMembers.TryGetValue(trackerItem.DatasetId, out MapMember? value))
      {
        // add layer and layer URI to tracker
        trackerItem.AddConvertedMapMember(value);
        trackerItem.AddLayerURI(value.URI);
        conversionTracker[item.Key] = trackerItem; // not necessary atm, but needed if we use conversionTracker further
        // only add a report item
        AddResultsFromTracker(trackerItem, results);
      }
      else
      {
        // add layer to Map
        MapMember mapMember = AddDatasetsToMap(trackerItem, createdLayerGroups);

        // add layer and layer URI to tracker
        trackerItem.AddConvertedMapMember(mapMember);
        trackerItem.AddLayerURI(mapMember.URI);
        conversionTracker[item.Key] = trackerItem; // not necessary atm, but needed if we use conversionTracker further

        // add layer URI to bakedIds
        bakedObjectIds.Add(trackerItem.MappedLayerURI == null ? "" : trackerItem.MappedLayerURI);

        // mark dataset as already created
        bakedMapMembers[trackerItem.DatasetId] = mapMember;

        // add report item
        AddResultsFromTracker(trackerItem, results);
      }
      onOperationProgressed?.Invoke("Adding to Map", (double)++bakeCount / conversionTracker.Count);
    }
    bakedObjectIds.AddRange(createdLayerGroups.Values.Select(x => x.URI));

    // TODO: validated a correct set regarding bakedobject ids
    return new(bakedObjectIds, results);
  }

  private void AddResultsFromTracker(ObjectConversionTracker trackerItem, List<ReceiveConversionResult> results)
  {
    if (trackerItem.MappedLayerURI == null) // should not happen
    {
      results.Add(
        new(
          Status.ERROR,
          trackerItem.Base,
          null,
          null,
          new ArgumentException($"Created Layer URI not found for {trackerItem.Base.speckle_type}")
        )
      );
    }
    else
    {
      // encode layer ID and ID of its feature in 1 object represented as string
      ObjectID objectId = new(trackerItem.MappedLayerURI, trackerItem.DatasetRow);
      if (trackerItem.HostAppGeom != null) // individual hostAppGeometry
      {
        results.Add(
          new(
            Status.SUCCESS,
            trackerItem.Base,
            objectId.ObjectIdToString(),
            trackerItem.HostAppGeom.GetType().ToString()
          )
        );
      }
      else // hostApp Layers
      {
        results.Add(
          new(
            Status.SUCCESS,
            trackerItem.Base,
            objectId.ObjectIdToString(),
            trackerItem.HostAppMapMember?.GetType().ToString()
          )
        );
      }
    }
  }

  private MapMember AddDatasetsToMap(
    ObjectConversionTracker trackerItem,
    Dictionary<string, GroupLayer> createdLayerGroups
  )
  {
    // get layer details
    string? datasetId = trackerItem.DatasetId; // should not be null here
    Uri uri = new($"{_contextStack.Current.Document.SpeckleDatabasePath.AbsolutePath.Replace('/', '\\')}\\{datasetId}");
    string nestedLayerName = trackerItem.NestedLayerName;

    // add group for the current layer
    string shortName = nestedLayerName.Split("\\")[^1];
    string nestedLayerPath = string.Join("\\", nestedLayerName.Split("\\").SkipLast(1));

    GroupLayer groupLayer = CreateNestedGroupLayer(nestedLayerPath, createdLayerGroups);

    // Most of the Speckle-written datasets will be containing geometry and added as Layers
    // although, some datasets might be just tables (e.g. native GIS Tables, in the future maybe Revit schedules etc.
    // We can create a connection to the dataset in advance and determine its type, but this will be more
    // expensive, than assuming by default that it's a layer with geometry (which in most cases it's expected to be)
    try
    {
      var layer = LayerFactory.Instance.CreateLayer(uri, groupLayer, layerName: shortName);
      layer.SetExpanded(true);
      return layer;
    }
    catch (ArgumentException)
    {
      var table = StandaloneTableFactory.Instance.CreateStandaloneTable(uri, groupLayer, tableName: shortName);
      return table;
    }
  }

  private GroupLayer CreateNestedGroupLayer(string nestedLayerPath, Dictionary<string, GroupLayer> createdLayerGroups)
  {
    GroupLayer lastGroup = createdLayerGroups.FirstOrDefault().Value;
    if (lastGroup == null) // if layer not found
    {
      throw new InvalidOperationException("Speckle Layer Group not found");
    }

    // iterate through each nested level
    string createdGroupPath = "";
    var allPathElements = nestedLayerPath.Split("\\").Where(x => !string.IsNullOrEmpty(x));
    foreach (string pathElement in allPathElements)
    {
      createdGroupPath += "\\" + pathElement;
      if (createdLayerGroups.TryGetValue(createdGroupPath, out var existingGroupLayer))
      {
        lastGroup = existingGroupLayer;
      }
      else
      {
        // create new GroupLayer under last found Group, named with last pathElement
        lastGroup = LayerFactory.Instance.CreateGroupLayer(lastGroup, 0, pathElement);
        lastGroup.SetExpanded(true);
      }
      createdLayerGroups[createdGroupPath] = lastGroup;
    }
    return lastGroup;
  }

  [Pure]
  private static string[] GetLayerPath(TraversalContext context)
  {
    string[] collectionBasedPath = context.GetAscendantOfType<Collection>().Select(c => c.name).ToArray();
    string[] reverseOrderPath =
      collectionBasedPath.Length != 0 ? collectionBasedPath : context.GetPropertyPath().ToArray();

    var originalPath = reverseOrderPath.Reverse().ToArray();
    return originalPath.Where(x => !string.IsNullOrEmpty(x)).ToArray();
  }

  [Pure]
  private static bool HasGISParent(TraversalContext context)
  {
    List<Base> gisLayers = context.GetAscendants().Where(IsGISType).Where(obj => obj != context.Current).ToList();
    return gisLayers.Count > 0;
  }

  [Pure]
  private static bool IsGISType(Base obj)
  {
    return obj is RasterLayer or VectorLayer;
  }
}
