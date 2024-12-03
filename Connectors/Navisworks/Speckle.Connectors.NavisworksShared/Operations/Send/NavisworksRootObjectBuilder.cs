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

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<NAV.ModelItem> navisworksModelItems,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken = default
  )
  {
    using var activity = _activityFactory.Start("Build");

    // Initialize root collection
    var name = NavisworksApp.ActiveDocument.Title ?? "Unnamed model";

    var rootObjectCollection = new Collection
    {
      name = name,
      ["units"] = _converterSettings.Current.Derived.SpeckleUnits
    };

    if (!navisworksModelItems.Any() || navisworksModelItems == null)
    {
      throw new SpeckleException("No objects to convert");
    }

    List<SendConversionResult> results = new(navisworksModelItems.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (var navisworksItem in navisworksModelItems)
      {
        using var _2 = _activityFactory.Start("Convert");
        cancellationToken.ThrowIfCancellationRequested();

        if (sendInfo == null)
        {
          continue;
        }

        var result = ConvertNavisworksItem(navisworksItem, rootObjectCollection, sendInfo.ProjectId);
        results.Add(result);

        ++count;
        onOperationProgressed?.Report(new CardProgress("Converting", (double)count / navisworksModelItems.Count));
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    await Task.Yield();
    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private SendConversionResult ConvertNavisworksItem(
    NAV.ModelItem navisworksItem,
    Collection collectionHost,
    string projectId
  )
  {
    string applicationId = ResolveModelItemToIndexPath(navisworksItem);
    string sourceType = navisworksItem.GetType().Name;

    try
    {
      Base converted =
        // Check cache first
        _sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value)
          ? value
          // Convert geometry
          : _rootToSpeckleConverter.Convert(navisworksItem);

      collectionHost.elements.Add(converted);

      return new SendConversionResult(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert model item {id}", applicationId);
      return new SendConversionResult(Status.ERROR, applicationId, "ModelItem", null, ex);
    }
  }
}
