using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Operations.Diagnostics;
using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using static Speckle.Connector.Navisworks.Operations.Send.GeometryNodeMerger;

namespace Speckle.Connector.Navisworks.Operations.Send;

public class NavisworksRootObjectBuilder(
  IRootToSpeckleConverter rootToSpeckleConverter,
  ISendConversionCache sendConversionCache,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  ILogger<NavisworksRootObjectBuilder> logger,
  ISdkActivityFactory activityFactory,
  NavisworksMaterialUnpacker materialUnpacker,
  NavisworksColorUnpacker colorUnpacker,
  IElementSelectionService elementSelectionService,
  Speckle.Converter.Navisworks.Constants.Registers.IInstanceFragmentRegistry instanceRegistry
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
    // This is a temporary workaround to disable node merging for debugging purposes - false is default, true is for debugging
    SkipNodeMerging = false;

    // Set to true to disable grouping routines and test if they're interfering with instancing
    DisableGroupingForInstanceTesting = false;
#endif
    using var activity = activityFactory.Start("Build");

    ValidateInputs(navisworksModelItems, projectId, onOperationProgressed);

    // 2. Initialize the root collection
    var rootCollection = InitializeRootCollection();

    // 3. Convert all model items and store results
    (Dictionary<string, Base?> convertedElements, List<SendConversionResult> conversionResults) =
      await ConvertModelItemsAsync(navisworksModelItems, projectId, onOperationProgressed, cancellationToken);

    ValidateConversionResults(conversionResults);

    var groupedNodes = SkipNodeMerging ? [] : GroupSiblingGeometryNodes(navisworksModelItems);
    var finalElements = BuildFinalElements(convertedElements, groupedNodes);

    await AddProxiesToCollection(rootCollection, navisworksModelItems, groupedNodes);

    // Add instance definitions and geometry definitions collection
    AddInstanceDefinitionsToCollection(rootCollection, ref finalElements);

    // Diagnostic: Count InstanceProxy objects in final output
    int finalInstanceProxyCount = CountInstanceProxiesRecursive(finalElements);
    logger.LogInformation(
      "Final output contains {count} InstanceProxy objects in displayValues",
      finalInstanceProxyCount
    );

    // DIAGNOSTICS: Generate and log instance grouping report
    LogInstanceGroupingDiagnostics(navisworksModelItems.Count);

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

      // Count InstanceProxy objects for diagnostics
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
    // First build the grouped nodes as before (unless disabled for testing)
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

    // If hierarchy mode is enabled, reorganize into proper nested structure
    if (converterSettings.Current.User.PreserveModelHierarchy)
    {
      logger.LogInformation("Building hierarchy (PreserveModelHierarchy=true)");
      var hierarchyBuilder = new NavisworksHierarchyBuilder(
        convertedBases,
        rootToSpeckleConverter,
        elementSelectionService
      );

      var hierarchy = hierarchyBuilder.BuildHierarchy();

      return hierarchy;
    }

    // Otherwise continue with flat mode
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

  private (string name, string path) GetContext(string applicationId)
  {
    var modelItem = elementSelectionService.GetModelItemFromPath(applicationId);
    var context = HierarchyHelper.ExtractContext(modelItem);
    return (context.Name, context.Path);
  }

  /// <summary>
  /// Processes and adds any remaining non-grouped elements.
  /// </summary>
  /// <remarks>
  /// Handles both Collection and Base type elements differently.
  /// Only processes elements not handled in grouped processing.
  /// </remarks>
  private NavisworksObject CreateNavisworksObject(string groupKey, List<Base> siblingBases)
  {
    string cleanParentPath = ElementSelectionHelper.GetCleanPath(groupKey);
    (string name, string path) = GetContext(cleanParentPath);

    // Pre-calculate capacity to avoid list resizing during SelectMany
    int estimatedCapacity = siblingBases.Sum(b => (b["displayValue"] as List<Base>)?.Count ?? 0);
    var displayValues = new List<Base>(estimatedCapacity);
    foreach (var sibling in siblingBases)
    {
      if (sibling["displayValue"] is List<Base> displayValueList)
      {
        displayValues.AddRange(displayValueList);
      }
    }

    // Diagnostic: count InstanceProxy objects in merged group
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
      applicationId = groupKey, // Use the full composite key as applicationId to preserve uniqueness
      ["path"] = path
    };
  }

  /// <summary>
  /// Creates a NavisworksObject from a single converted base.
  /// </summary>
  /// <param name="convertedBase">The converted Speckle Base object.</param>
  /// <returns>A new NavisworksObject containing the converted data.</returns>
  private NavisworksObject? CreateNavisworksObject(Base convertedBase)
  {
    if (convertedBase.applicationId == null)
    {
      return null;
    }

    (string name, string path) = GetContext(convertedBase.applicationId);

    return new NavisworksObject
    {
      name = name,
      displayValue = convertedBase["displayValue"] as List<Base> ?? [],
      properties = convertedBase["properties"] as Dictionary<string, object?> ?? [],
      units = converterSettings.Current.Derived.SpeckleUnits,
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
      rootCollection[ProxyKeys.RENDER_MATERIAL] = renderMaterials;
    }

    var colors = colorUnpacker.UnpackColor(navisworksModelItems, groupedNodes);
    if (colors.Count > 0)
    {
      rootCollection[ProxyKeys.COLOR] = colors;
    }

    return Task.CompletedTask;
  }

  private void AddInstanceDefinitionsToCollection(Collection rootCollection, ref List<Base> finalElements)
  {
    using var _ = activityFactory.Start("BuildInstanceDefinitions");

    // Get all definition geometries from registry
    var allDefinitions = instanceRegistry.GetAllDefinitionGeometries();

    if (allDefinitions.Count == 0)
    {
      // No instancing - return early
      logger.LogInformation("No instance definitions found - instancing may be disabled");
      return;
    }

    logger.LogInformation("Building instance structure for {count} definition groups", allDefinitions.Count);

    // DIAGNOSTICS: Warn if we have too many definitions (indicates grouping failure)
    if (allDefinitions.Count > 100)
    {
      logger.LogWarning(
        "Large number of definition groups ({count}) detected - this may indicate instance grouping is not working effectively",
        allDefinitions.Count
      );
    }

    // Build InstanceDefinitionProxy objects
    var instanceDefinitionProxies = new List<InstanceDefinitionProxy>(allDefinitions.Count);

    // Estimate total geometry count for capacity hint (reduces list resizing)
    int estimatedGeometryCount = allDefinitions.Sum(kvp => kvp.Value.Count);
    var allDefinitionGeometries = new List<Base>(estimatedGeometryCount);

    foreach (var kvp in allDefinitions)
    {
      var groupKey = kvp.Key;
      var geometries = kvp.Value;
      var groupKeyHash = groupKey.ToHashString();

      // Create InstanceDefinitionProxy
      var defProxy = new InstanceDefinitionProxy
      {
        name = $"Shared Geometry {groupKeyHash}",
        objects = geometries.Select(g => g.applicationId ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList(),
        applicationId = $"def_{groupKeyHash}",
        maxDepth = 0
      };

      instanceDefinitionProxies.Add(defProxy);
      allDefinitionGeometries.AddRange(geometries);
    }

    // Add instanceDefinitionProxies to the root collection
    rootCollection["instanceDefinitionProxies"] = instanceDefinitionProxies;

    // Create a "Geometry Definitions" Collection
    var geometryDefinitionsCollection = new Collection
    {
      name = "Geometry Definitions",
      // collectionType = "GeometryDefinitions", // Deprecated
      elements = allDefinitionGeometries
    };

    // Create a bare Collection for the NavisworksObjects
    var objectCollection = new Collection
    {
      name = "",
      // collectionType = "GeometryDefinitions", // Deprecated
      elements = finalElements
    };

    // Prepend Geometry Definitions Collection to finalElements
    finalElements = [geometryDefinitionsCollection, objectCollection];

    logger.LogInformation(
      "Added {proxyCount} instance definition proxies and {geomCount} definition geometries",
      instanceDefinitionProxies.Count,
      allDefinitionGeometries.Count
    );
  }

  /// <summary>
  /// Recursively counts InstanceProxy objects in a collection hierarchy.
  /// </summary>
  private int CountInstanceProxiesRecursive(List<Base> elements)
  {
    int count = 0;
    foreach (var element in elements)
    {
      if (element["displayValue"] is List<Base> displayValues)
      {
        count += displayValues.Count(dv => dv.GetType().Name == "InstanceProxy");
      }

      if (element is Collection collection && collection.elements != null)
      {
        count += CountInstanceProxiesRecursive(collection.elements);
      }
    }
    return count;
  }

  /// <summary>
  /// Converts a single Navisworks item to a Speckle object.
  /// </summary>
  /// <remarks>
  /// Attempts to retrieve from the cache first.
  /// Falls back to fresh conversion if not cached.
  /// Logs errors but doesn't throw exceptions.
  /// </remarks>
  /// <returns>A SendConversionResult indicating success or failure.</returns>
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

  /// <summary>
  /// DIAGNOSTICS: Collects and logs instance grouping statistics from the registry.
  /// Helps diagnose why large selections may not be grouping correctly.
  /// </summary>
  private void LogInstanceGroupingDiagnostics(int totalItemsSelected)
  {
#if DEBUG
    try
    {
      // Collect statistics from registry
      var groupToConvertedPaths = instanceRegistry.BuildGroupToConvertedPaths();
      var diagnosticsBuilder = new InstanceGroupingDiagnosticsBuilder();

      foreach (var kvp in groupToConvertedPaths)
      {
        diagnosticsBuilder.RecordGroup(kvp.Key, kvp.Value.Count);
      }

      var diagnostics = diagnosticsBuilder.Build();

      // Log summary
      logger.LogInformation("╔════════════════════════════════════════════════════════════════╗");
      logger.LogInformation("║  INSTANCE GROUPING DIAGNOSTICS                                ║");
      logger.LogInformation("╚════════════════════════════════════════════════════════════════╝");
      logger.LogInformation(
        "Items selected: {selected}, Groups created: {groups}",
        totalItemsSelected,
        diagnostics.TotalGroupsCreated
      );
      logger.LogInformation("{summary}", diagnostics.GenerateSummary());

      // Check if grouping is effective
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
}
