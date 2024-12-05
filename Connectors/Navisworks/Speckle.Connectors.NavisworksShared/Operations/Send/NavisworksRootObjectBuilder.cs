using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using static Speckle.Connector.Navisworks.Extensions.ElementSelectionExtension;

namespace Speckle.Connector.Navisworks.Operations.Send;

public class NavisworksRootObjectBuilder : IRootObjectBuilder<NAV.ModelItem>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _converterSettings;
  private readonly ILogger<NavisworksRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  public NavisworksRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
    ILogger<NavisworksRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory
  )
  {
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _logger = logger;
    _activityFactory = activityFactory;
  }

  internal NavisworksConversionSettings GetCurrentSettings() => _converterSettings.Current;

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken = default
  )
  {
    using var activity = _activityFactory.Start("Build");

    // 1. Validate input
    if (!navisworksModelItems.Any())
    {
      throw new SpeckleException("No objects to convert");
    }

    // 2. Initialize root collection
    var rootObjectCollection = new Collection
    {
      name = NavisworksApp.ActiveDocument.Title ?? "Unnamed model",
      ["units"] = _converterSettings.Current.Derived.SpeckleUnits
    };

    // 3. Convert all model items and store results
    var convertedBases = new Dictionary<string, Base?>();
    var results = new List<SendConversionResult>();
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

      var parentBase = new Base
      {
        applicationId = group.Key,
        ["properties"] = siblingBases.First()["properties"],
        ["name"] = siblingBases.First()["name"],
        ["displayValue"] = siblingBases.SelectMany(b => b["displayValue"] as List<Base> ?? []).ToList()
      };
      finalElements.Add(parentBase);
    }

    // 6. Add remaining non-grouped nodes
    foreach (var result in results.Where(result => !processedPaths.Contains(result.SourceId)))
    {
      if (convertedBases.TryGetValue(result.SourceId, out var convertedBase) && convertedBase != null)
      {
        finalElements.Add(convertedBase);
      }
    }

    // 7. Finalize and return
    rootObjectCollection.elements = finalElements;
    return Task.FromResult(new RootObjectBuilderResult(rootObjectCollection, results));
  }

  internal SendConversionResult ConvertNavisworksItem(
    NAV.ModelItem navisworksItem,
    Dictionary<string, Base?> convertedBases,
    SendInfo sendInfo
  )
  {
    string applicationId = ResolveModelItemToIndexPath(navisworksItem);
    string sourceType = navisworksItem.GetType().Name;

    try
    {
      Base converted = _sendConversionCache.TryGetValue(applicationId, sendInfo.ProjectId, out ObjectReference? cached)
        ? cached
        : _rootToSpeckleConverter.Convert(navisworksItem);

      convertedBases[applicationId] = converted;

      return new SendConversionResult(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert model item {id}", applicationId);
      return new SendConversionResult(Status.ERROR, applicationId, "ModelItem", null, ex);
    }
  }
}
