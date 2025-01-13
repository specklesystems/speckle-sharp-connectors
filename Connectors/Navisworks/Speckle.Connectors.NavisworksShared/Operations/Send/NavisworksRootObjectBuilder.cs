using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
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
  IElementSelectionService elementSelectionService
) : IRootObjectBuilder<NAV.ModelItem>
{
  private bool SkipNodeMerging { get; set; }

  internal NavisworksConversionSettings GetCurrentSettings() => converterSettings.Current;

  public async Task<RootObjectBuilderResult> BuildAsync(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
#if DEBUG
    // This is a temporary workaround to disable node merging for debugging purposes - false is default, true is for debugging
    SkipNodeMerging = false;
#endif
    using var activity = activityFactory.Start("Build");

    // 1. Validate input
    if (!navisworksModelItems.Any())
    {
      throw new SpeckleException("No objects to convert");
    }

    // 2. Initialize root collection
    var rootObjectCollection = new Collection
    {
      name = NavisworksApp.ActiveDocument.Title ?? "Unnamed model",
      ["units"] = converterSettings.Current.Derived.SpeckleUnits
    };

    // 3. Convert all model items and store results
    if (navisworksModelItems == null)
    {
      throw new ArgumentNullException(nameof(navisworksModelItems));
    }

    List<SendConversionResult> results = new(navisworksModelItems.Count);
    var convertedBases = new Dictionary<string, Base?>();
    int processedCount = 0;
    int totalCount = navisworksModelItems.Count;

    if (onOperationProgressed == null || sendInfo == null)
    {
      throw new ArgumentNullException(nameof(onOperationProgressed));
    }

    foreach (var item in navisworksModelItems)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var converted = ConvertNavisworksItem(item, convertedBases, sendInfo);
      results.Add(converted);
      processedCount++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)processedCount / totalCount));
      await Task.Yield();
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
    }

    // 4. Initialize final elements list and group nodes
    var finalElements = new List<Base>();
    var groupedNodes = SkipNodeMerging ? [] : GroupSiblingGeometryNodes(navisworksModelItems);
    var processedPaths = new HashSet<string>();

    // 5. Process and merge grouped nodes
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

      if (siblingBases.Count == 0)
      {
        continue;
      }

      var navisworksObject = new NavisworksObject
      {
        name = elementSelectionService.GetModelItemFromPath(group.Key).DisplayName ?? string.Empty,
        displayValue = siblingBases.SelectMany(b => b["displayValue"] as List<Base> ?? []).ToList(),
        properties = siblingBases.First()["properties"] as Dictionary<string, object?> ?? [],
        units = converterSettings.Current.Derived.SpeckleUnits,
        applicationId = group.Key
      };

      finalElements.Add(navisworksObject);
    }

    // 6. Add remaining non-grouped nodes
    foreach (var result in results.Where(result => !processedPaths.Contains(result.SourceId)))
    {
      if (!convertedBases.TryGetValue(result.SourceId, out var convertedBase) || convertedBase == null)
      {
        continue;
      }
      // TODO: check if converted base is a collection when full tree sending is implemented

      if (convertedBase is Collection convertedCollection)
      {
        finalElements.Add(convertedCollection);
      }
      else
      {
        var navisworksObject = new NavisworksObject
        {
          name = convertedBase["name"] as string ?? string.Empty,
          displayValue = convertedBase["displayValue"] as List<Base> ?? [],
          properties = convertedBase["properties"] as Dictionary<string, object?> ?? [],
          units = converterSettings.Current.Derived.SpeckleUnits,
          applicationId = convertedBase.applicationId
        };
        finalElements.Add(navisworksObject);
      }
    }

    using (var _ = activityFactory.Start("UnpackRenderMaterials"))
    {
      // 7.  - Unpack the render material proxies
      rootObjectCollection[ProxyKeys.RENDER_MATERIAL] = materialUnpacker.UnpackRenderMaterial(
        navisworksModelItems,
        groupedNodes
      );
    }

    // 8. Finalize and return
    rootObjectCollection.elements = finalElements;
    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private SendConversionResult ConvertNavisworksItem(
    NAV.ModelItem navisworksItem,
    Dictionary<string, Base?> convertedBases,
    SendInfo sendInfo
  )
  {
    string applicationId = elementSelectionService.GetModelItemPath(navisworksItem);
    string sourceType = navisworksItem.GetType().Name;

    try
    {
      Base converted = sendConversionCache.TryGetValue(applicationId, sendInfo.ProjectId, out ObjectReference? cached)
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
