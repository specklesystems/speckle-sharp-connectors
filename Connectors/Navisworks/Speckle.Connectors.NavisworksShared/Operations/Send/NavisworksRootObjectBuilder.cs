using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using static Speckle.Connector.Navisworks.Extensions.ElementSelectionExtension;
using static Speckle.Connector.Navisworks.Extensions.NavisworksRootObjectBuilderExtensions;

namespace Speckle.Connector.Navisworks.Operations.Send;

public class NavisworksRootObjectBuilder : IRootObjectBuilder<NAV.ModelItem>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _converterSettings;
  private readonly ILogger<NavisworksRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly PropertySetsExtractor _propertySetsExtractor;

  public NavisworksRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
    ILogger<NavisworksRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ClassPropertiesExtractor classPropertiesExtractor,
    PropertySetsExtractor propertySetsExtractor
  )
  {
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _logger = logger;
    _activityFactory = activityFactory;
    _classPropertiesExtractor = classPropertiesExtractor;
    _propertySetsExtractor = propertySetsExtractor;
  }

  internal NavisworksConversionSettings GetCurrentSettings() => _converterSettings.Current;

  public async Task<RootObjectBuilderResult> Build(
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

    return await BuildWithMergedSiblings(
        navisworksModelItems,
        onOperationProgressed,
        _rootToSpeckleConverter,
        _classPropertiesExtractor,
        _propertySetsExtractor,
        _converterSettings,
        cancellationToken
      )
      .ConfigureAwait(false);
  }

  internal SendConversionResult ConvertNavisworksItem(
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
