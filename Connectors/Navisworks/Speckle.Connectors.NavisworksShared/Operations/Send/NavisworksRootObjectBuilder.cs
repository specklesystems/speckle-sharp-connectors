using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Converter.Navisworks.Constants.Registers;
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
  IInstanceFragmentRegistry instanceRegistry,
  IElementSelectionService elementSelectionService,
  GeometryConversionContext geometryConversionContext,
  IUiUnitsCache uiUnitsCache,
  IQuickPropertyDefinitionsCache quickPropertyDefinitionsCache,
  NavisworksSendBenchmarkLogger benchmarkLogger
) : IRootObjectBuilder<NAV.ModelItem>
{
  private readonly Dictionary<string, (string Name, string Path)> _elementNameAndPathCache = new(
    StringComparer.Ordinal
  );

  private bool SkipNodeMerging { get; set; }

  private bool DisableGroupingForInstanceTesting { get; set; }

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
    quickPropertyDefinitionsCache.Reset();
    GeometryConversionMetricsTracker.Reset();
    MeshOptimizationMetricsTracker.Reset();
    _elementNameAndPathCache.Clear();
    NavisworksGcSnapshot gcSnapshot = NavisworksGcSnapshot.Capture();
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
    benchmarkLogger.LogConversionMetrics();
    benchmarkLogger.LogBuildBenchmarkSummary(
      conversionStartMs,
      conversionEndMs,
      reassemblyStopwatch.Elapsed.TotalMilliseconds,
      finalElements.Count,
      gcSnapshot
    );

    rootCollection.elements = finalElements;
    return new RootObjectBuilderResult(rootCollection, conversionResults);
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

    onOperationProgressed.Report(new CardProgress("Converting", 0));

    int visibleGeometryCount = CountVisibleGeometryItems(navisworksModelItems);
    double geometryWeight =
      visibleGeometryCount == 0
        ? 0
        : Math.Min(Math.Max(visibleGeometryCount / (double)Math.Max(totalCount, 1), 0.75), 0.95);

    geometryConversionContext.PrimeBatch(
      navisworksModelItems,
      geometryWeight > 0
        ? (fraction, pathsProcessed) =>
          onOperationProgressed.Report(
            new CardProgress($"Converting (Path {pathsProcessed:N0})", fraction * geometryWeight)
          )
        : null
    );
    try
    {
      const int ITEM_PROGRESS_REPORT_INTERVAL = 1000;

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
        if (processedCount % ITEM_PROGRESS_REPORT_INTERVAL != 0 && processedCount != totalCount)
        {
          continue;
        }

        double itemProgress = geometryWeight + (1 - geometryWeight) * processedCount / totalCount;
        onOperationProgressed.Report(new CardProgress("Converting", itemProgress));
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
      foreach (var itemPath in group.Value.Select(t => elementSelectionService.GetModelItemPath(t)))
      {
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
    foreach (var kvp in convertedBases.Where(kvp => !processedPaths.Contains(kvp.Key)))
    {
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

  private int CountVisibleGeometryItems(IReadOnlyList<NAV.ModelItem> items)
  {
    int count = 0;
    foreach (var item in items)
    {
      if (item.HasGeometry && elementSelectionService.IsVisible(item))
      {
        count++;
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
#pragma warning disable CA1823
#pragma warning restore CA1823
}
