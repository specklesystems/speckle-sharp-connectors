using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Services;
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
  InstanceStoreManager instanceStoreManager
) : IRootObjectBuilder<NAV.ModelItem>
{
  private bool SkipNodeMerging { get; set; }

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
    SkipNodeMerging = true;
#endif
    using var activity = activityFactory.Start("Build");

    ValidateInputs(navisworksModelItems, projectId, onOperationProgressed);

    // 2. Initialize root collection
    var rootCollection = InitializeRootCollection();

    // InstanceStoreManager is scoped - starts fresh for each conversion session

    // 3. Convert all model items and store results
    (Dictionary<string, Base?> convertedElements, List<SendConversionResult> conversionResults) =
      await ConvertModelItemsAsync(navisworksModelItems, projectId, onOperationProgressed, cancellationToken);

    ValidateConversionResults(conversionResults);

    var groupedNodes = SkipNodeMerging ? [] : GroupSiblingGeometryNodes(navisworksModelItems);
    var finalElements = BuildFinalElements(convertedElements, groupedNodes);
    List<Base> geometryDefinitions = instanceStoreManager.GetGeometryDefinitions();

    await AddProxiesToCollection(rootCollection, navisworksModelItems, groupedNodes);

    // rootCollection.elements will contain two Collections: one for geometry definitions and one for the main elements

    var geometryDefinitionsCollection = new Collection
    {
      name = "Geometry Definitions",
      ["units"] = converterSettings.Current.Derived.SpeckleUnits,
      elements = geometryDefinitions
    };

    var mainElementsCollection = new Collection
    {
      name = rootCollection.name,
      ["units"] = converterSettings.Current.Derived.SpeckleUnits,
      elements = finalElements
    };

    rootCollection.elements = [mainElementsCollection];
    if (geometryDefinitions.Count > 0)
    {
      rootCollection.elements.Add(geometryDefinitionsCollection);
    }

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

    foreach (var item in navisworksModelItems)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var converted = ConvertNavisworksItem(item, convertedBases, projectId);
      results.Add(converted);
      processedCount++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)processedCount / totalCount));
    }

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
    // First build the grouped nodes as before
    var finalElements = new List<Base>();
    var processedPaths = new HashSet<string>();
    AddGroupedElements(finalElements, convertedBases, groupedNodes, processedPaths);

    // If hierarchy mode is enabled, reorganize into proper nested structure
    if (converterSettings.Current.User.PreserveModelHierarchy)
    {
      var hierarchyBuilder = new NavisworksHierarchyBuilder(
        convertedBases,
        rootToSpeckleConverter,
        elementSelectionService
      );

      var hierarchy = hierarchyBuilder.BuildHierarchy();

      return hierarchy;
    }

    // Otherwise continue with flat mode
    AddRemainingElements(finalElements, convertedBases, processedPaths);
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
      var siblingBases = new List<Base>();
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
  /// Only processes elements that weren't handled in grouped processing.
  /// </remarks>
  private NavisworksObject CreateNavisworksObject(string groupKey, List<Base> siblingBases)
  {
    string cleanParentPath = ElementSelectionHelper.GetCleanPath(groupKey);
    (string name, string path) = GetContext(cleanParentPath);

    return new NavisworksObject
    {
      name = name,
      displayValue = siblingBases.SelectMany(b => b["displayValue"] as List<Base> ?? []).ToList(),
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

    // Add instance definition proxies from dual store
    var instanceDefinitionProxies = instanceStoreManager.GetInstanceDefinitionProxies();
    logger.LogDebug(
      "Retrieved {Count} instance definition proxies from store",
      instanceDefinitionProxies.Count
    );

    if (instanceDefinitionProxies.Count > 0)
    {
      rootCollection[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies.ToList();
      logger.LogDebug(
        "Added {Count} instance definition proxies to root collection under key '{Key}'",
        instanceDefinitionProxies.Count,
        ProxyKeys.INSTANCE_DEFINITION
      );
    }
    else
    {
      logger.LogDebug("No instance definition proxies to add to root collection");
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Converts a single Navisworks item to a Speckle object.
  /// </summary>
  /// <remarks>
  /// Attempts to retrieve from cache first.
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
}
