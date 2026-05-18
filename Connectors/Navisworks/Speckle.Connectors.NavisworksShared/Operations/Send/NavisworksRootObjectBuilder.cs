using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Data;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;
using static Speckle.Connector.Navisworks.Operations.Send.GeometryNodeMerger;
using static Speckle.Connectors.Common.Operations.ProxyKeys;
using static Speckle.Converter.Navisworks.Constants.InstanceConstants;

namespace Speckle.Connector.Navisworks.Operations.Send;

public class NavisworksRootObjectBuilder(
  IRootToSpeckleConverter rootToSpeckleConverter,
  ISendConversionCache sendConversionCache,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  ILogger<NavisworksRootObjectBuilder> logger,
  ISdkActivityFactory activityFactory,
  NavisworksMaterialUnpacker materialUnpacker,
  NavisworksColorUnpacker colorUnpacker,
  Speckle.Converter.Navisworks.Constants.Registers.IInstanceFragmentRegistry instanceRegistry,
  IElementSelectionService elementSelectionService,
  GeometryConversionContext geometryConversionContext,
  IUiUnitsCache uiUnitsCache
) : IRootObjectBuilder<NAV.ModelItem>
{
#pragma warning disable CA1823
#pragma warning restore CA1823
  private bool SkipNodeMerging { get; set; }
  private bool DisableGroupingForInstanceTesting { get; set; }
  private readonly Dictionary<string, (string Name, string Path)> _elementNameAndPathCache = new(
    StringComparer.Ordinal
  );

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
#if DEBUG
    SkipNodeMerging = false;
    DisableGroupingForInstanceTesting = false;
#endif
    PropertyExtractionMetricsTracker.Reset();
    GeometryConversionMetricsTracker.Reset();
    MeshOptimizationMetricsTracker.Reset();
    _elementNameAndPathCache.Clear();
    int gcGen0Start = GC.CollectionCount(0);
    int gcGen1Start = GC.CollectionCount(1);
    int gcGen2Start = GC.CollectionCount(2);
    long managedHeapBytesStart = GC.GetTotalMemory(false);
    using var activity = activityFactory.Start("Build");

    ValidateInputs(navisworksModelItems, projectId, onOperationProgressed);

    var rootCollection = InitializeRootCollection();
    long conversionStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    (Dictionary<string, Base?> convertedElements, List<SendConversionResult> conversionResults) =
      await ConvertModelItemsAsync(navisworksModelItems, projectId, onOperationProgressed, cancellationToken);
    long conversionEndMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    ValidateConversionResults(conversionResults);

    var reassemblyStopwatch = Stopwatch.StartNew();
    var groupedNodes = SkipNodeMerging ? [] : GroupSiblingGeometryNodes(navisworksModelItems);
    var finalElements = BuildFinalElements(convertedElements, groupedNodes);
    var twoDElementPaths = Build2DElementPathSet(convertedElements);

    await AddProxiesToCollection(rootCollection, navisworksModelItems, groupedNodes, twoDElementPaths);

    AddInstanceDefinitionsToCollection(rootCollection, ref finalElements);
    reassemblyStopwatch.Stop();
    int finalInstanceProxyCount = CountInstanceProxiesRecursive(finalElements);
    logger.LogInformation(
      "Final output contains {count} InstanceProxy objects in displayValues",
      finalInstanceProxyCount
    );
    LogPropertyExtractionMetrics();
    LogGeometryConversionMetrics();
    LogMeshOptimizationMetrics();
    LogBenchmarkSummary(
      conversionStartMs,
      conversionEndMs,
      reassemblyStopwatch.Elapsed.TotalMilliseconds,
      finalElements.Count,
      gcGen0Start,
      gcGen1Start,
      gcGen2Start,
      managedHeapBytesStart
    );

    rootCollection.elements = finalElements;
    return new RootObjectBuilderResult(rootCollection, conversionResults);
  }

  private void LogPropertyExtractionMetrics()
  {
    var snapshot = PropertyExtractionMetricsTracker.Snapshot();
    logger.LogInformation(
      "Property extraction metrics: objects={ObjectCount}, avgTotalCategories={AvgTotalCategoryCount:F2}, avgUserFilteredCategories={AvgUserFilteredCategoryCount:F2}, avgProperties={AvgPropertyCount:F2}, p95Properties={P95PropertyCount:F0}, avgPayloadBytes={AvgPayloadBytes:F2}, p95PayloadBytes={P95PayloadBytes:F0}, totalPayloadBytes={TotalPayloadBytes}, avgExtractionMs={AvgExtractionMs:F2}, p95ExtractionMs={P95ExtractionMs:F2}",
      snapshot.ObjectCount,
      snapshot.AvgTotalCategoryCount,
      snapshot.AvgUserFilteredCategoryCount,
      snapshot.AvgPropertyCount,
      snapshot.P95PropertyCount,
      snapshot.AvgPayloadBytes,
      snapshot.P95PayloadBytes,
      snapshot.TotalPayloadBytes,
      snapshot.AvgExtractionMs,
      snapshot.P95ExtractionMs
    );
  }

  private void LogGeometryConversionMetrics()
  {
    var snapshot = GeometryConversionMetricsTracker.Snapshot();
    logger.LogInformation(
      "Geometry conversion metrics: toInwOpSelectionCalls={ToInwOpSelectionCalls}, convertedObjects={ConvertedObjectCount}, pathsProcessed={PathsProcessed}, fragmentsProcessed={FragmentsProcessed}, totalSelectionMs={TotalSelectionElapsedMs:F2}, avgSelectionMs={AvgSelectionElapsedMs:F2}, p95SelectionMs={P95SelectionElapsedMs:F2}, avgSelectionMsPerObject={AvgSelectionElapsedMsPerObject:F4}, avgPathsPerSelection={AvgPathsPerSelection:F2}, avgFragmentsPerPath={AvgFragmentsPerPath:F2}",
      snapshot.ToInwOpSelectionCalls,
      snapshot.ConvertedObjectCount,
      snapshot.PathsProcessed,
      snapshot.FragmentsProcessed,
      snapshot.TotalSelectionElapsedMs,
      snapshot.AvgSelectionElapsedMs,
      snapshot.P95SelectionElapsedMs,
      snapshot.AvgSelectionElapsedMsPerObject,
      snapshot.AvgPathsPerSelection,
      snapshot.AvgFragmentsPerPath
    );
  }

  private void LogMeshOptimizationMetrics()
  {
    var snapshot = MeshOptimizationMetricsTracker.Snapshot();
    logger.LogInformation(
      "Mesh optimization metrics: meshObjectCount={MeshObjectCount}, emptyGeometryObjectCount={EmptyGeometryObjectCount}, faceCount={FaceCount}, lineCount={LineCount}, vertexCountBeforeWeld={VertexCountBeforeWeld}, vertexCountAfterWeld={VertexCountAfterWeld}, vertexReductionPercent={VertexReductionPercent:F2}, meshWeldMs={MeshWeldMs:F2}, avgVerticesPerObject={AvgVerticesPerObject:F2}, geometryDetailLevel={GeometryDetailLevel}, seamRetentionEnabled={SeamRetentionEnabled}, creaseAngleDegrees={CreaseAngleDegrees:F1}",
      snapshot.MeshObjectCount,
      snapshot.EmptyGeometryObjectCount,
      snapshot.FaceCount,
      snapshot.LineCount,
      snapshot.VertexCountBeforeWeld,
      snapshot.VertexCountAfterWeld,
      snapshot.VertexReductionPercent,
      snapshot.MeshWeldMs,
      snapshot.AvgVerticesPerObject,
      snapshot.GeometryDetailLevel,
      snapshot.SeamRetentionEnabled,
      snapshot.CreaseAngleDegrees
    );
  }

  private void LogBenchmarkSummary(
    long conversionStartMs,
    long conversionEndMs,
    double reassemblyMs,
    int finalElementCount,
    int gcGen0Start,
    int gcGen1Start,
    int gcGen2Start,
    long managedHeapBytesStart
  )
  {
    var user = converterSettings.Current.User;
    var propertySnapshot = PropertyExtractionMetricsTracker.Snapshot();
    var meshSnapshot = MeshOptimizationMetricsTracker.Snapshot();
    var conversionMs = conversionEndMs - conversionStartMs;
    int gcGen0Delta = GC.CollectionCount(0) - gcGen0Start;
    int gcGen1Delta = GC.CollectionCount(1) - gcGen1Start;
    int gcGen2Delta = GC.CollectionCount(2) - gcGen2Start;
    long managedHeapBytesEnd = GC.GetTotalMemory(false);

    logger.LogInformation(
      "Build benchmark summary: geometryPreset={GeometryPreset}, propertyPreset={PropertyPreset}, roundMeshVertexDoubles={RoundMeshVertexDoubles}, includeInternalProperties={IncludeInternalProperties}, preserveHierarchy={PreserveHierarchy}, seamRetentionEnabled={SeamRetentionEnabled}, creaseAngleDegrees={CreaseAngleDegrees:F1}, conversionMs={ConversionMs}, reassemblyMs={ReassemblyMs:F2}, totalMeasuredMs={TotalMeasuredMs:F2}, finalElementCount={FinalElementCount}, propertyObjectCount={PropertyObjectCount}, avgPropertiesPerObject={AvgPropertiesPerObject:F2}, p95PropertiesPerObject={P95PropertiesPerObject:F0}, meshObjectCount={MeshObjectCount}, vertexCountBeforeWeld={VertexCountBeforeWeld}, vertexCountAfterWeld={VertexCountAfterWeld}, vertexReductionPercent={VertexReductionPercent:F2}, managedHeapBytesStart={ManagedHeapBytesStart}, managedHeapBytesEnd={ManagedHeapBytesEnd}, gcCollectionsGen0={GcCollectionsGen0}, gcCollectionsGen1={GcCollectionsGen1}, gcCollectionsGen2={GcCollectionsGen2}",
      user.GeometryDetailLevel,
      user.PropertyDetailLevel,
      user.RoundMeshVertexDoubles,
      user.IncludeInternalProperties,
      user.PreserveModelHierarchy,
      meshSnapshot.SeamRetentionEnabled,
      meshSnapshot.CreaseAngleDegrees,
      conversionMs,
      reassemblyMs,
      conversionMs + reassemblyMs,
      finalElementCount,
      propertySnapshot.ObjectCount,
      propertySnapshot.AvgPropertyCount,
      propertySnapshot.P95PropertyCount,
      meshSnapshot.MeshObjectCount,
      meshSnapshot.VertexCountBeforeWeld,
      meshSnapshot.VertexCountAfterWeld,
      meshSnapshot.VertexReductionPercent,
      managedHeapBytesStart,
      managedHeapBytesEnd,
      gcGen0Delta,
      gcGen1Delta,
      gcGen2Delta
    );
  }

  private static void ValidateInputs(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    string projectId,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    if (!navisworksModelItems.Any())
    {
      throw new SpeckleException("No objects to convert");
    }

    if (navisworksModelItems == null)
    {
      throw new ArgumentNullException(nameof(navisworksModelItems));
    }

    if (onOperationProgressed == null || projectId == null)
    {
      throw new ArgumentNullException(
        onOperationProgressed == null ? nameof(onOperationProgressed) : nameof(projectId)
      );
    }
  }

  private Collection InitializeRootCollection() =>
    new()
    {
      name = NavisworksApp.ActiveDocument.Title ?? "Unnamed model",
      ["units"] = converterSettings.Current.Derived.SpeckleUnits,
    };

  private Task<(Dictionary<string, Base?> converted, List<SendConversionResult> results)> ConvertModelItemsAsync(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var results = new List<SendConversionResult>(navisworksModelItems.Count);
    var convertedBases = new Dictionary<string, Base?>();
    int processedCount = 0;
    int totalCount = navisworksModelItems.Count;
    int instanceProxyCount = 0;

    geometryConversionContext.PrimeBatch(navisworksModelItems);
    try
    {
      foreach (var item in navisworksModelItems)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var converted = ConvertNavisworksItem(item, convertedBases, projectId);
        results.Add(converted);

        if (
          converted.Status == Status.SUCCESS
          && convertedBases.TryGetValue(elementSelectionService.GetModelItemPath(item), out var convertedBase)
          && convertedBase?["displayValue"] is List<Base> displayValues
        )
        {
          instanceProxyCount += displayValues.Count(dv => dv.GetType().Name == "InstanceProxy");
        }

        processedCount++;
        onOperationProgressed.Report(new CardProgress("Converting", (double)processedCount / totalCount));
      }
    }
    finally
    {
      geometryConversionContext.Clear();
    }

    logger.LogInformation(
      "Converted {total} items, found {instanceProxies} InstanceProxy objects",
      totalCount,
      instanceProxyCount
    );
    return Task.FromResult((convertedBases, results));
  }

  private static void ValidateConversionResults(List<SendConversionResult> results)
  {
    var allErrored = results.All(t => t.Status == Status.ERROR);

    if (allErrored)
    {
      throw new SpeckleException("Failed to convert all objects.");
    }
  }

  private List<Base> BuildFinalElements(
    Dictionary<string, Base?> convertedBases,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes
  )
  {
    var finalElements = new List<Base>(convertedBases.Count);
    var processedPaths = new HashSet<string>(convertedBases.Count, StringComparer.Ordinal);

    if (!DisableGroupingForInstanceTesting)
    {
      AddGroupedElements(finalElements, convertedBases, groupedNodes, processedPaths);
      logger.LogInformation(
        "After grouping: {grouped} paths processed, {elements} elements in collection",
        processedPaths.Count,
        finalElements.Count
      );
    }
    else
    {
      logger.LogInformation("Grouping disabled for instance testing");
    }

    if (converterSettings.Current.User.PreserveModelHierarchy)
    {
      logger.LogInformation("Building hierarchy (PreserveModelHierarchy=true)");
      var hierarchyBuilder = new NavisworksHierarchyBuilder(
        convertedBases,
        rootToSpeckleConverter,
        elementSelectionService
      );

      return hierarchyBuilder.BuildHierarchy();
    }

    logger.LogInformation("Adding remaining elements (flat mode)");
    AddRemainingElements(finalElements, convertedBases, processedPaths);

    logger.LogInformation("Final elements count: {count}", finalElements.Count);
    return finalElements;
  }

  private void AddGroupedElements(
    List<Base> finalElements,
    Dictionary<string, Base?> convertedBases,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes,
    HashSet<string> processedPaths
  )
  {
    foreach (var group in groupedNodes)
    {
      var siblingBases = new List<Base>(group.Value.Count);
      foreach (var t in group.Value)
      {
        var itemPath = elementSelectionService.GetModelItemPath(t);
        processedPaths.Add(itemPath);
        if (convertedBases.TryGetValue(itemPath, out var convertedBase) && convertedBase != null)
        {
          siblingBases.Add(convertedBase);
        }
      }

      if (siblingBases.Count > 0)
      {
        finalElements.Add(CreateNavisworksObject(group.Key, siblingBases));
      }
    }
  }

  private void AddRemainingElements(
    List<Base> finalElements,
    Dictionary<string, Base?> convertedBases,
    HashSet<string> processedPaths
  )
  {
    foreach (var kvp in convertedBases)
    {
      if (processedPaths.Contains(kvp.Key))
      {
        continue;
      }

      switch (kvp.Value)
      {
        case null:
          continue;
        case Collection collection:
          finalElements.Add(collection);
          break;
        default:
          if (CreateNavisworksObject(kvp.Value) is { } navisworksObject)
          {
            finalElements.Add(navisworksObject);
          }

          break;
      }
    }
  }

  private (string name, string path) GetElementNameAndPath(string applicationId)
  {
    if (_elementNameAndPathCache.TryGetValue(applicationId, out var cached))
    {
      return (cached.Name, cached.Path);
    }

    var modelItem = elementSelectionService.GetModelItemFromPath(applicationId);
    var context = HierarchyHelper.ExtractContext(modelItem);
    _elementNameAndPathCache[applicationId] = (context.Name, context.Path);
    return (context.Name, context.Path);
  }

  private NavisworksObject CreateNavisworksObject(string groupKey, List<Base> siblingBases)
  {
    string cleanParentPath = ElementSelectionHelper.GetCleanPath(groupKey);
    (string name, string path) = GetElementNameAndPath(cleanParentPath);

    var estimatedCapacity = 0;
    foreach (var t in siblingBases)
    {
      if (t["displayValue"] is List<Base> siblingDisplayValues)
      {
        estimatedCapacity += siblingDisplayValues.Count;
      }
    }

    var displayValues = new List<Base>(estimatedCapacity);
    var instanceProxyCount = 0;
    foreach (var t in siblingBases)
    {
      if (t["displayValue"] is not List<Base> siblingDisplayValues)
      {
        continue;
      }

      foreach (var displayValue in siblingDisplayValues)
      {
        displayValues.Add(displayValue);
        if (displayValue is InstanceProxy)
        {
          instanceProxyCount++;
        }
      }
    }

    if (instanceProxyCount > 0)
    {
      logger.LogDebug(
        "Group {groupKey} merging {siblings} siblings with {proxies} InstanceProxy objects",
        groupKey,
        siblingBases.Count,
        instanceProxyCount
      );
    }

    return new NavisworksObject
    {
      name = name,
      displayValue = displayValues,
      properties = siblingBases[0]["properties"] as Dictionary<string, object?> ?? [],
      units = converterSettings.Current.Derived.SpeckleUnits,
      applicationId = groupKey,
      ["path"] = path,
    };
  }

  private NavisworksObject? CreateNavisworksObject(Base convertedBase)
  {
    if (convertedBase.applicationId == null)
    {
      return null;
    }

    (string name, string path) = GetElementNameAndPath(convertedBase.applicationId);

    var units = uiUnitsCache.Ensure();

    return new NavisworksObject
    {
      name = name,
      displayValue = convertedBase["displayValue"] as List<Base> ?? [],
      properties = convertedBase["properties"] as Dictionary<string, object?> ?? [],
      units = units.ToString(),
      applicationId = convertedBase.applicationId,
      ["path"] = path,
    };
  }

  private Task AddProxiesToCollection(
    Collection rootCollection,
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes,
    ISet<string> twoDElementPaths
  )
  {
    using var _ = activityFactory.Start("UnpackProxies");

    var renderMaterials = materialUnpacker.UnpackRenderMaterial(navisworksModelItems, groupedNodes);
    if (renderMaterials.Count > 0)
    {
      rootCollection[RENDER_MATERIAL] = renderMaterials;
    }

    var colors = colorUnpacker.UnpackColor(navisworksModelItems, groupedNodes, twoDElementPaths);
    if (colors.Count > 0)
    {
      rootCollection[COLOR] = colors;
    }

    return Task.CompletedTask;
  }

  private static HashSet<string> Build2DElementPathSet(Dictionary<string, Base?> convertedBases)
  {
    var twoDElementPaths = new HashSet<string>();

    foreach (var kvp in convertedBases)
    {
      var path = kvp.Key;
      var convertedBase = kvp.Value;
      if (convertedBase?["displayValue"] is not List<Base> displayValues || displayValues.Count == 0)
      {
        continue;
      }

      bool hasMesh = displayValues.Any(x => x is Mesh);
      bool hasLine = displayValues.Any(x => x is Line);
      bool hasInstanceProxy = displayValues.Any(x => x is InstanceProxy);

      if (!hasMesh && hasLine && !hasInstanceProxy)
      {
        twoDElementPaths.Add(path);
      }
    }

    return twoDElementPaths;
  }

  private void AddInstanceDefinitionsToCollection(Collection rootCollection, ref List<Base> finalElements)
  {
    using var _ = activityFactory.Start("BuildInstanceDefinitions");

    // Get all definition geometries from the registry
    var allDefinitions = instanceRegistry.GetAllDefinitionGeometries();

    if (allDefinitions.Count == 0)
    {
      logger.LogInformation("No instance definitions found - instancing may be disabled");
      return;
    }

    logger.LogInformation("Building instance structure for {count} definition groups", allDefinitions.Count);

    if (allDefinitions.Count > 100)
    {
      logger.LogWarning(
        "Large number of definition groups ({count}) detected - this may indicate instance grouping is not working effectively",
        allDefinitions.Count
      );
    }

    var instanceDefinitionProxies = new List<InstanceDefinitionProxy>(allDefinitions.Count);

    int estimatedGeometryCount = allDefinitions.Sum(kvp => kvp.Value.Count);
    var allDefinitionGeometries = new List<Base>(estimatedGeometryCount);

    foreach (var kvp in allDefinitions)
    {
      var groupKey = kvp.Key;
      var geometries = kvp.Value;
      var groupKeyPath = groupKey.ToPathString();

      var defProxy = new InstanceDefinitionProxy
      {
        name = $"Shared Geometry {groupKeyPath}",
        objects = geometries.Select(g => g.applicationId ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList(),
        applicationId = $"{DEFINITION_ID_PREFIX}{groupKeyPath}",
        maxDepth = 0,
      };

      instanceDefinitionProxies.Add(defProxy);
      allDefinitionGeometries.AddRange(geometries);
    }

    rootCollection[INSTANCE_DEFINITION] = instanceDefinitionProxies;
    var geometryDefinitionsCollection = new Collection
    {
      name = "Geometry Definitions",
      elements = allDefinitionGeometries,
    };

    var objectCollection = new Collection { name = "", elements = finalElements };

    finalElements = [geometryDefinitionsCollection, objectCollection];

    logger.LogInformation(
      "Added {proxyCount} instance definition proxies and {geomCount} definition geometries",
      instanceDefinitionProxies.Count,
      allDefinitionGeometries.Count
    );
  }

  private int CountInstanceProxiesRecursive(List<Base> elements)
  {
    int count = 0;
    foreach (var element in elements)
    {
      if (element["displayValue"] is List<Base> displayValues)
      {
        count += displayValues.Count(dv => dv.GetType().Name == "InstanceProxy");
      }

      if (element is Collection { elements: not null } collection)
      {
        count += CountInstanceProxiesRecursive(collection.elements);
      }
    }
    return count;
  }

  private SendConversionResult ConvertNavisworksItem(
    NAV.ModelItem navisworksItem,
    Dictionary<string, Base?> convertedBases,
    string projectId
  )
  {
    string applicationId = elementSelectionService.GetModelItemPath(navisworksItem);
    string sourceType = navisworksItem.GetType().Name;

    try
    {
      Base converted = sendConversionCache.TryGetValue(applicationId, projectId, out ObjectReference? cached)
        ? cached
        : rootToSpeckleConverter.Convert(navisworksItem);

      convertedBases[applicationId] = converted;

      return new SendConversionResult(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogError(ex, "Failed to convert model item {id}", applicationId);
      return new SendConversionResult(Status.ERROR, applicationId, "ModelItem", null, ex);
    }
  }
}
