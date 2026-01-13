using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Operations.Diagnostics;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
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
  Speckle.Converter.Navisworks.ToSpeckle.DisplayValueExtractor displayValueExtractor,
  IElementSelectionService elementSelectionService,
  IUiUnitsCache uiUnitsCache
) : IRootObjectBuilder<NAV.ModelItem>
{
  private bool SkipNodeMerging { get; set; }
  private bool DisableGroupingForInstanceTesting { get; set; }

  internal NavisworksConversionSettings GetCurrentSettings() => converterSettings.Current;

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
    using var activity = activityFactory.Start("Build");

    ValidateInputs(navisworksModelItems, projectId, onOperationProgressed);

    var rootCollection = InitializeRootCollection();
    (Dictionary<string, Base?> convertedElements, List<SendConversionResult> conversionResults) =
      await ConvertModelItemsAsync(navisworksModelItems, projectId, onOperationProgressed, cancellationToken);

    ValidateConversionResults(conversionResults);

    var groupedNodes = SkipNodeMerging ? [] : GroupSiblingGeometryNodes(navisworksModelItems);
    var finalElements = BuildFinalElements(convertedElements, groupedNodes);

    await AddProxiesToCollection(rootCollection, navisworksModelItems, groupedNodes);

    AddInstanceDefinitionsToCollection(rootCollection, ref finalElements);
    int finalInstanceProxyCount = CountInstanceProxiesRecursive(finalElements);
    logger.LogInformation(
      "Final output contains {count} InstanceProxy objects in displayValues",
      finalInstanceProxyCount
    );

    LogInstanceGroupingDiagnostics(navisworksModelItems.Count);
    LogGeometryCacheStatistics();

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
      ["units"] = converterSettings.Current.Derived.SpeckleUnits
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

    foreach (var item in navisworksModelItems)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var converted = ConvertNavisworksItem(item, convertedBases, projectId);
      results.Add(converted);

      if (
        converted.Status == Status.SUCCESS
        && convertedBases.TryGetValue(elementSelectionService.GetModelItemPath(item), out var convertedBase)
        && convertedBase != null
      )
      {
        if (convertedBase["displayValue"] is List<Base> displayValues)
        {
          instanceProxyCount += displayValues.Count(dv => dv.GetType().Name == "InstanceProxy");
        }
      }

      processedCount++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)processedCount / totalCount));
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
    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }
  }

  private List<Base> BuildFinalElements(
    Dictionary<string, Base?> convertedBases,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes
  )
  {
    var finalElements = new List<Base>();
    var processedPaths = new HashSet<string>();

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
      foreach (var itemPath in group.Value.Select(elementSelectionService.GetModelItemPath))
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
    var modelItem = elementSelectionService.GetModelItemFromPath(applicationId);
    var context = HierarchyHelper.ExtractContext(modelItem);
    return (context.Name, context.Path);
  }

  private NavisworksObject CreateNavisworksObject(string groupKey, List<Base> siblingBases)
  {
    string cleanParentPath = ElementSelectionHelper.GetCleanPath(groupKey);
    (string name, string path) = GetElementNameAndPath(cleanParentPath);

    int estimatedCapacity = siblingBases.Sum(b => (b["displayValue"] as List<Base>)?.Count ?? 0);
    var displayValues = new List<Base>(estimatedCapacity);
    foreach (var sibling in siblingBases)
    {
      if (sibling["displayValue"] is List<Base> displayValueList)
      {
        displayValues.AddRange(displayValueList);
      }
    }

    var instanceProxyCount = displayValues.Count(dv => dv.GetType().Name == "InstanceProxy");
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
      properties = siblingBases.First()["properties"] as Dictionary<string, object?> ?? [],
      units = converterSettings.Current.Derived.SpeckleUnits,
      applicationId = groupKey,
      ["path"] = path
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
      ["path"] = path
    };
  }

  private Task AddProxiesToCollection(
    Collection rootCollection,
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    Dictionary<string, List<NAV.ModelItem>> groupedNodes
  )
  {
    using var _ = activityFactory.Start("UnpackProxies");

    var renderMaterials = materialUnpacker.UnpackRenderMaterial(navisworksModelItems, groupedNodes);
    if (renderMaterials.Count > 0)
    {
      rootCollection[RENDER_MATERIAL] = renderMaterials;
    }

    var colors = colorUnpacker.UnpackColor(navisworksModelItems, groupedNodes);
    if (colors.Count > 0)
    {
      rootCollection[COLOR] = colors;
    }

    return Task.CompletedTask;
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
      var groupKeyHash = groupKey.ToHashString();

      var defProxy = new InstanceDefinitionProxy
      {
        name = $"Shared Geometry {groupKeyHash}",
        objects = geometries.Select(g => g.applicationId ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList(),
        applicationId = $"{DEFINITION_ID_PREFIX}{groupKeyHash}",
        maxDepth = 0
      };

      instanceDefinitionProxies.Add(defProxy);
      allDefinitionGeometries.AddRange(geometries);
    }

    rootCollection[INSTANCE_DEFINITION] = instanceDefinitionProxies;
    var geometryDefinitionsCollection = new Collection
    {
      name = "Geometry Definitions",
      elements = allDefinitionGeometries
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

  private void LogInstanceGroupingDiagnostics(int totalItemsSelected)
  {
#if DEBUG
    try
    {
      var groupToConvertedPaths = instanceRegistry.BuildGroupToConvertedPaths();
      var diagnosticsBuilder = new InstanceGroupingDiagnosticsBuilder();

      foreach (var kvp in groupToConvertedPaths)
      {
        diagnosticsBuilder.RecordGroup(kvp.Key, kvp.Value.Count);
      }

      var diagnostics = diagnosticsBuilder.Build();

      logger.LogInformation("╔════════════════════════════════════════════════════════════════╗");
      logger.LogInformation("║  INSTANCE GROUPING DIAGNOSTICS                                ║");
      logger.LogInformation("╚════════════════════════════════════════════════════════════════╝");
      logger.LogInformation(
        "Items selected: {selected}, Groups created: {groups}",
        totalItemsSelected,
        diagnostics.TotalGroupsCreated
      );
      logger.LogInformation("{summary}", diagnostics.GenerateSummary());

      if (!diagnostics.IsGroupingEffective())
      {
        logger.LogWarning("⚠️  Instance grouping appears to be INEFFECTIVE!");
        logger.LogWarning("Most items are in single-member groups (no instancing detected).");

        var recommendations = diagnostics.GetRecommendations();
        foreach (var recommendation in recommendations)
        {
          logger.LogWarning("  - {recommendation}", recommendation);
        }
      }
      else
      {
        logger.LogInformation(
          "✓ Instance grouping is working: {multiMember} groups with multiple instances",
          diagnostics.MultiMemberGroups
        );
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed to generate instance grouping diagnostics");
    }
#endif
  }

  private void LogGeometryCacheStatistics()
  {
#if DEBUG
    try
    {
      var geometryConverter = displayValueExtractor.GeometryConverter;

      logger.LogInformation("╔════════════════════════════════════════════════════════════════╗");
      logger.LogInformation("║  GEOMETRY CACHE PERFORMANCE                                   ║");
      logger.LogInformation("╚════════════════════════════════════════════════════════════════╝");

      var (comMs, geometryMs, itemCount) = geometryConverter.GetPerformanceStatistics();
      if (itemCount > 0)
      {
        logger.LogInformation("Performance Timing:");
        logger.LogInformation("  Items Processed: {count}", itemCount);
        logger.LogInformation(
          "  COM Extraction: {comMs:F2} ms ({percent:F1}%)",
          comMs,
          comMs / (comMs + geometryMs) * 100
        );
        logger.LogInformation(
          "  Geometry Creation: {geomMs:F2} ms ({percent:F1}%)",
          geometryMs,
          geometryMs / (comMs + geometryMs) * 100
        );
        logger.LogInformation(
          "  Avg per item: {avgCom:F3} ms COM, {avgGeom:F3} ms Geometry",
          comMs / itemCount,
          geometryMs / itemCount
        );
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed to generate geometry cache statistics");
    }
#endif
  }
}
