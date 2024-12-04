using Speckle.Connector.Navisworks.Operations.Send;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using static Speckle.Converter.Navisworks.ToSpeckle.DisplayValueExtractor;

namespace Speckle.Connector.Navisworks.Extensions;

public static class NavisworksRootObjectBuilderExtensions
{
  public static Task<RootObjectBuilderResult> BuildWithMergedSiblings(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    IProgress<CardProgress> onOperationProgressed,
    IRootToSpeckleConverter rootToSpeckleConverter,
    ClassPropertiesExtractor classPropertiesExtractor,
    PropertySetsExtractor propertySetsExtractor,
    IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
    CancellationToken cancellationToken = default
  )
  {
    var rootObjectCollection = new Collection
    {
      name = NavisworksApp.ActiveDocument.Title ?? "Unnamed model",
      ["units"] = settingsStore.Current.Derived.SpeckleUnits
    };

    var merger = new GeometryNodeMerger();
    var groupedNodes = merger.GroupSiblingGeometryNodes(navisworksModelItems);

    List<SendConversionResult> results = new();
    int processedCount = 0;
    int totalItems = groupedNodes.Count + (navisworksModelItems.Count - groupedNodes.Sum(g => g.Value.Count));

    // Handle merged groups
    foreach (var group in groupedNodes)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var parentItem = group.Value[0].Parent;
      if (parentItem == null)
      {
        continue;
      }

      var displayValues = group.Value.SelectMany(GetDisplayValue).ToList();

      var mergedBase = new Base { ["displayValue"] = displayValues, applicationId = group.Key };

      var classProperties = classPropertiesExtractor.GetClassProperties(parentItem);
      if (classProperties != null)
      {
        foreach (var kvp in classProperties)
        {
          mergedBase[kvp.Key] = kvp.Value;
        }
      }

      var propertySets = propertySetsExtractor.GetPropertySets(parentItem);
      if (propertySets != null)
      {
        mergedBase["properties"] = propertySets;
      }

      rootObjectCollection.elements.Add(mergedBase);
      results.Add(new SendConversionResult(Status.SUCCESS, group.Key, "MergedGeometry", mergedBase));

      ReportProgress(ref processedCount, totalItems, onOperationProgressed);
    }

    // Handle ungrouped nodes
    HashSet<NAV.ModelItem> groupedNodeSet = [.. groupedNodes.SelectMany(g => g.Value)];
    foreach (var node in navisworksModelItems.Where(n => !groupedNodeSet.Contains(n)))
    {
      Base converted = rootToSpeckleConverter.Convert(node);
      var applicationId = ElementSelectionExtension.ResolveModelItemToIndexPath(node);

      var result = new SendConversionResult(Status.SUCCESS, applicationId, node.GetType().Name, converted);

      rootObjectCollection.elements.Add(converted);
      results.Add(result);
      ReportProgress(ref processedCount, totalItems, onOperationProgressed);
    }

    return Task.FromResult(new RootObjectBuilderResult(rootObjectCollection, results));
  }

  private static void ReportProgress(ref int count, int total, IProgress<CardProgress>? progress)
  {
    count++;
    progress?.Report(new CardProgress("Converting", (double)count / total));
  }
}
