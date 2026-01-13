using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converter.Navisworks.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManagerNavisworks(ISendConversionCache sendConversionCache)
  : IToSpeckleSettingsManagerNavisworks
{
  // cache invalidation process run with ModelCardId since the settings are model-specific
  private readonly Dictionary<string, RepresentationMode> _visualRepresentationCache = [];
  private readonly Dictionary<string, OriginMode> _originModeCache = [];
  private readonly Dictionary<string, bool> _convertHiddenElementsCache = [];
  private readonly Dictionary<string, bool> _includeInternalPropertiesCache = [];
  private readonly Dictionary<string, bool> _preserveModelHierarchyCache = [];
  private readonly Dictionary<string, bool> _revitCategoryMappingCache = [];

  /// <summary>
  /// Generic helper to get a setting value with caching and cache invalidation.
  /// </summary>
  private T GetCachedSetting<T>(
    SenderModelCard modelCard,
    string settingId,
    Dictionary<string, T> cache,
    Func<object?, T> valueExtractor,
    T defaultValue
  )
  {
    if (modelCard == null)
    {
      throw new ArgumentNullException(nameof(modelCard));
    }

    var settingValue = modelCard.Settings?.FirstOrDefault(s => s.Id == settingId)?.Value;
    var returnValue = settingValue != null ? valueExtractor(settingValue) : defaultValue;

    if (
      cache.TryGetValue(modelCard.ModelCardId.NotNull(), out var previousValue)
      && !EqualityComparer<T>.Default.Equals(previousValue, returnValue)
    )
    {
      EvictCacheForModelCard(modelCard);
    }

    cache[modelCard.ModelCardId.NotNull()] = returnValue;
    return returnValue;
  }

  public RepresentationMode GetVisualRepresentationMode(SenderModelCard modelCard) =>
    GetCachedSetting(
      modelCard,
      "visualRepresentation",
      _visualRepresentationCache,
      value =>
      {
        var representationString = value as string;
        return
          representationString is not null
          && VisualRepresentationSetting.VisualRepresentationMap.TryGetValue(
            representationString,
            out RepresentationMode representation
          )
          ? representation
          : throw new ArgumentException($"Invalid visual representation value: {representationString}");
      },
      RepresentationMode.Active // default value if setting not found
    );

  public OriginMode GetOriginMode(SenderModelCard modelCard) =>
    GetCachedSetting(
      modelCard,
      "originMode",
      _originModeCache,
      value =>
      {
        var originString = value as string;
        if (OriginModeSetting.OriginModeMap.TryGetValue(originString ?? string.Empty, out var origin))
        {
          return origin;
        }
        return OriginMode.ModelOrigin;
      },
      OriginMode.ModelOrigin
    );

  public bool GetMappingToRevitCategories(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "mappingToRevitCategories", _revitCategoryMappingCache, value => value is true, false);

  public bool GetConvertHiddenElements(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "convertHiddenElements", _convertHiddenElementsCache, value => value is true, false);

  public bool GetIncludeInternalProperties(SenderModelCard modelCard) =>
    GetCachedSetting(
      modelCard,
      "includeInternalProperties",
      _includeInternalPropertiesCache,
      value => value is true,
      false
    );

  public bool GetPreserveModelHierarchy(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "preserveModelHierarchy", _preserveModelHierarchyCache, value => value is true, false);

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    sendConversionCache.EvictObjects(objectIds);
  }
}
