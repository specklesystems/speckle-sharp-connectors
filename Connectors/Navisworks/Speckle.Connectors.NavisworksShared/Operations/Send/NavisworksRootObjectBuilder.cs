using Microsoft.Extensions.Logging;
using Speckle.Connector.Navisworks.Extensions;
using Speckle.Connector.Navisworks.HostApp;
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
using static Speckle.Connector.Navisworks.Extensions.ElementSelectionExtension;

namespace Speckle.Connector.Navisworks.Operations.Send;

public class NavisworksRootObjectBuilder(
  IRootToSpeckleConverter rootToSpeckleConverter,
  ISendConversionCache sendConversionCache,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  ILogger<NavisworksRootObjectBuilder> logger,
  ISdkActivityFactory activityFactory,
  NavisworksMaterialUnpacker materialUnpacker
) : IRootObjectBuilder<NAV.ModelItem>
{
  internal NavisworksConversionSettings GetCurrentSettings() => converterSettings.Current;

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken = default
  )
  {
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
    List<SendConversionResult> results = new(navisworksModelItems.Count);
    var convertedBases = new Dictionary<string, Base?>();
    int processedCount = 0;
    int totalCount = navisworksModelItems.Count;

    foreach (var item in navisworksModelItems)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var converted = ConvertNavisworksItem(item, convertedBases, sendInfo);
      results.Add(converted);
      processedCount++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)processedCount / totalCount));
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects."); // fail fast instead creating empty commit! It will appear as model card error with red color.
    }

    // 4. Initialize final elements list and group nodes
    var finalElements = new List<Base>();
    var merger = new GeometryNodeMerger();
    var groupedNodes = merger.GroupSiblingGeometryNodes(navisworksModelItems);
    var processedPaths = new HashSet<string>();

    // 5. Process and merge grouped nodes
    foreach (var group in groupedNodes)
    {
      var siblingBases = new List<Base>();
      foreach (var itemPath in group.Value.Select(ResolveModelItemToIndexPath))
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
        name = ElementSelectionExtension.ResolveIndexPathToModelItem(group.Key)?.DisplayName ?? string.Empty,
        displayValue = siblingBases.SelectMany(b => b["displayValue"] as List<Base> ?? []).ToList(),
        properties = siblingBases.First()["properties"] as Dictionary<string, object?> ?? [],
        units = converterSettings.Current.Derived.SpeckleUnits
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
          units = converterSettings.Current.Derived.SpeckleUnits
        };
        finalElements.Add(navisworksObject);
      }
    }

    using (var _ = activityFactory.Start("UnpackRenderMaterials"))
    {
      // 7.  - Unpack the render material proxies
      rootObjectCollection[ProxyKeys.RENDER_MATERIAL] = materialUnpacker.UnpackRenderMaterial(navisworksModelItems);
    }

    // 8. Finalize and return
    rootObjectCollection.elements = finalElements;
    return Task.FromResult(new RootObjectBuilderResult(rootObjectCollection, results));
  }

  private SendConversionResult ConvertNavisworksItem(
    NAV.ModelItem navisworksItem,
    Dictionary<string, Base?> convertedBases,
    SendInfo sendInfo
  )
  {
    string applicationId = ResolveModelItemToIndexPath(navisworksItem);
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
