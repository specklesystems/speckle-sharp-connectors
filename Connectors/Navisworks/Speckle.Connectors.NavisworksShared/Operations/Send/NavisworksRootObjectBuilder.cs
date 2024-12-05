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

    if (!navisworksModelItems.Any())
    {
      throw new SpeckleException("No objects to convert");
    }

    // Create root collection
    var rootObjectCollection = new Collection
    {
      name = NavisworksApp.ActiveDocument.Title ?? "Unnamed model",
      ["units"] = _converterSettings.Current.Derived.SpeckleUnits
    };

    var convertedBases = new Dictionary<string, Base?>();

    // First convert everything and track results
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

    // Create final elements list
    var finalElements = new List<Base>();

    // Get groups of siblings
    var merger = new GeometryNodeMerger();
    var groupedNodes = merger.GroupSiblingGeometryNodes(navisworksModelItems);

    // Handle grouped nodes first
    foreach (var group in groupedNodes)
    {
      var siblingBases = convertedBases.Where(r => r.Key.StartsWith(group.Key)).Select(r => r.Value).ToList();

      if (siblingBases.Count == 0)
      {
        continue;
      }

      List<Base> displayValues = [];

      foreach (var siblingBase in siblingBases.OfType<Base>())
      {
        var dv = siblingBase["displayValue"];

        Console.WriteLine(dv);

        if (siblingBase["displayValue"] is not List<Base> displayValue)
        {
          continue;
        }

        displayValues.AddRange(displayValue);
      }

      var parentBase = new Base
      {
        applicationId = group.Key,
        ["properties"] = siblingBases.First()?["properties"],
        ["displayValue"] = displayValues
      };
      finalElements.Add(parentBase);
    }

    // Handle non-grouped nodes
    var groupedPaths = groupedNodes.SelectMany(g => g.Value).Select(ResolveModelItemToIndexPath).ToHashSet();

    foreach (var result in results.Where(result => !groupedPaths.Contains(result.SourceId)))
    {
      if (_sendConversionCache.TryGetValue(result.SourceId, sendInfo.ProjectId, out ObjectReference? value))
      {
        finalElements.Add(value);
      }
    }

    // Set the final elements list
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
