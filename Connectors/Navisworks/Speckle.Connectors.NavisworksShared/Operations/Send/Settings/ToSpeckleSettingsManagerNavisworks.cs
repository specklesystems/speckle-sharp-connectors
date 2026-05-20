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
  private readonly Dictionary<string, bool> _convertHiddenElementsCache = [];
  private readonly Dictionary<string, GeometryDetailLevel> _geometryDetailLevelCache = [];
  private readonly Dictionary<string, OriginMode> _originModeCache = [];
  private readonly Dictionary<string, bool> _preserveModelHierarchyCache = [];
  private readonly Dictionary<string, PropertyDetailLevel> _propertyDetailLevelCache = [];
  private readonly Dictionary<string, bool> _revitCategoryMappingCache = [];

  private readonly Dictionary<string, bool> _roundMeshVertexDoublesCache = [];

  // cache invalidation process run with ModelCardId since the settings are model-specific
  private readonly Dictionary<string, RepresentationMode> _visualRepresentationCache = [];

  public RepresentationMode GetVisualRepresentationMode(SenderModelCard modelCard) =>
    GetCachedSetting(
      modelCard,
      "visualRepresentation",
      _visualRepresentationCache,
      value =>
      {
        string? representationString = value as string;
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
        string? originString = value as string;
        if (OriginModeSetting.OriginModeMap.TryGetValue(originString ?? string.Empty, out OriginMode origin))
          return origin;

        return OriginMode.ModelOrigin;
      },
      OriginMode.ModelOrigin
    );

  public PropertyDetailLevel GetPropertyDetailLevel(SenderModelCard modelCard)
  {
    if (modelCard == null)
      throw new ArgumentNullException(nameof(modelCard));

    // Legacy cards: separate "excludeProperties" boolean maps to None.
    bool excludeLegacy = modelCard.Settings?.FirstOrDefault(s => s.Id == "excludeProperties")?.Value is true;
    PropertyDetailLevel effective = excludeLegacy
      ? PropertyDetailLevel.None
      : ResolvePropertyDetailLevelString(modelCard);

    if (
      _propertyDetailLevelCache.TryGetValue(modelCard.ModelCardId.NotNull(), out PropertyDetailLevel previousValue)
      && previousValue != effective
    )
      EvictCacheForModelCard(modelCard);

    _propertyDetailLevelCache[modelCard.ModelCardId.NotNull()] = effective;
    return effective;
  }

  public GeometryDetailLevel GetGeometryDetailLevel(SenderModelCard modelCard) =>
    GetCachedSetting(
      modelCard,
      "geometryDetailLevel",
      _geometryDetailLevelCache,
      value =>
      {
        string? geometryDetailString = value as string;
        if (
          geometryDetailString is not null
          && GeometryDetailLevelSetting.GeometryDetailLevelMap.TryGetValue(
            geometryDetailString,
            out GeometryDetailLevel detailLevel
          )
        )
          return detailLevel;

        return GeometryDetailLevel.Optimised;
      },
      GeometryDetailLevel.Optimised
    );

  public bool GetMappingToRevitCategories(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "mappingToRevitCategories", _revitCategoryMappingCache, value => value is true, false);

  public bool GetConvertHiddenElements(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "convertHiddenElements", _convertHiddenElementsCache, value => value is true, false);

  public bool GetIncludeInternalProperties(SenderModelCard modelCard)
  {
    if (modelCard == null)
      throw new ArgumentNullException(nameof(modelCard));

    return false;
  }

  public bool GetRoundMeshVertexDoubles(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "roundMeshVertexDoubles", _roundMeshVertexDoublesCache, value => value is true, false);

  public bool GetPreserveModelHierarchy(SenderModelCard modelCard) =>
    GetCachedSetting(modelCard, "preserveModelHierarchy", _preserveModelHierarchyCache, value => value is true, false);

  /// <summary>
  ///   Generic helper to get a setting value with caching and cache invalidation.
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
      throw new ArgumentNullException(nameof(modelCard));

    object? settingValue = modelCard.Settings?.FirstOrDefault(s => s.Id == settingId)?.Value;
    T? returnValue = settingValue != null ? valueExtractor(settingValue) : defaultValue;

    if (
      cache.TryGetValue(modelCard.ModelCardId.NotNull(), out T? previousValue)
      && !EqualityComparer<T>.Default.Equals(previousValue, returnValue)
    )
      EvictCacheForModelCard(modelCard);

    cache[modelCard.ModelCardId.NotNull()] = returnValue;
    return returnValue;
  }

  private static PropertyDetailLevel ResolvePropertyDetailLevelString(SenderModelCard modelCard)
  {
    string? propertyDetailString =
      modelCard.Settings?.FirstOrDefault(s => s.Id == "propertyDetailLevel")?.Value as string;
    if (
      propertyDetailString is not null
      && PropertyDetailLevelSetting.PropertyDetailLevelMap.TryGetValue(
        propertyDetailString,
        out PropertyDetailLevel detailLevel
      )
    )
      return detailLevel;

    return PropertyDetailLevel.Standard;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    List<string> objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    sendConversionCache.EvictObjects(objectIds);
  }
}
