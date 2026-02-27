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
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send;
using static Speckle.Connector.Navisworks.Operations.Send.GeometryNodeMerger;
using static Speckle.Connectors.Common.Operations.ProxyKeys;
using static Speckle.Converter.Navisworks.Constants.InstanceConstants;

namespace Speckle.Connector.Navisworks.Operations.Send;

/// <summary>
/// Continuous traversal builder for Navisworks that streams objects through a <see cref="SendPipeline"/>
/// for packfile-based uploads. Same conversion/grouping logic as <see cref="NavisworksRootObjectBuilder"/>,
/// but processes final elements through the pipeline after all post-processing is complete.
/// </summary>
public class NavisworksContinuousTraversalBuilder(
  IRootToSpeckleConverter rootToSpeckleConverter,
  ISendConversionCache sendConversionCache,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  ILogger<NavisworksContinuousTraversalBuilder> logger,
  ISdkActivityFactory activityFactory,
  NavisworksMaterialUnpacker materialUnpacker,
  NavisworksColorUnpacker colorUnpacker,
  Speckle.Converter.Navisworks.Constants.Registers.IInstanceFragmentRegistry instanceRegistry,
  IElementSelectionService elementSelectionService,
  IUiUnitsCache uiUnitsCache
) : IRootContinuousTraversalBuilder<NAV.ModelItem>
{
  private bool SkipNodeMerging { get; set; }
  private bool DisableGroupingForInstanceTesting { get; set; }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    string projectId,
    SendPipeline sendPipeline,
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

    // Process each final element through the send pipeline
    var processedElements = new List<Base>(finalElements.Count);
    foreach (var element in finalElements)
    {
      cancellationToken.ThrowIfCancellationRequested();
      // NOTE: this is the main part that differentiate from the main root object builder
      var reference = await sendPipeline.Process(element).ConfigureAwait(false);
      processedElements.Add(reference);
    }

    rootCollection.elements = processedElements;

    // Process the root collection and wait for all uploads to complete
    await sendPipeline.Process(rootCollection);
    await sendPipeline.WaitForUpload();

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
    displayValues.AddRange(
      siblingBases
        .Where(sibling => sibling["displayValue"] is List<Base>)
        .SelectMany(sibling => (List<Base>)sibling["displayValue"]!)
    );

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

    var allDefinitions = instanceRegistry.GetAllDefinitionGeometries();

    if (allDefinitions.Count == 0)
    {
      logger.LogInformation("No instance definitions found - instancing may be disabled");
      return;
    }

    logger.LogInformation("Building instance structure for {count} definition groups", allDefinitions.Count);

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
