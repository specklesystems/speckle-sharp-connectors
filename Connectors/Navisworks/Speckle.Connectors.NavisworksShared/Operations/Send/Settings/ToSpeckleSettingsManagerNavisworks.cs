using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converter.Navisworks.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManagerNavisworks : IToSpeckleSettingsManagerNavisworks
{
  private readonly ISendConversionCache _sendConversionCache;

  // cache invalidation process run with ModelCardId since the settings are model specific
  private readonly Dictionary<string, RepresentationMode> _visualRepresentationCache = [];
  private readonly Dictionary<string, OriginMode> _originModeCache = [];
  private readonly Dictionary<string, bool?> _convertHiddenElementsCache = [];
  private readonly Dictionary<string, bool?> _includeInternalPropertiesCache = [];
  private readonly Dictionary<string, bool?> _preserveModelHierarchyCache = [];

  public ToSpeckleSettingsManagerNavisworks(ISendConversionCache sendConversionCache)
  {
    _sendConversionCache = sendConversionCache;
  }

  public RepresentationMode GetVisualRepresentationMode(SenderModelCard modelCard)
  {
    if (modelCard == null)
    {
      throw new ArgumentNullException(nameof(modelCard));
    }

    var representationString = modelCard.Settings?.First(s => s.Id == "visualRepresentation").Value as string;

    if (
      representationString is not null
      && VisualRepresentationSetting.VisualRepresentationMap.TryGetValue(
        representationString,
        out RepresentationMode representation
      )
    )
    {
      if (_visualRepresentationCache.TryGetValue(modelCard.ModelCardId.NotNull(), out RepresentationMode previousType))
      {
        if (previousType != representation)
        {
          EvictCacheForModelCard(modelCard);
        }
      }

      _visualRepresentationCache[modelCard.ModelCardId.NotNull()] = representation;
      return representation;
    }

    throw new ArgumentException($"Invalid visual representation value: {representationString}");
  }

  public OriginMode GetOriginMode(SenderModelCard modelCard)
  {
    if (modelCard == null)
    {
      throw new ArgumentNullException(nameof(modelCard));
    }

    if (
      modelCard.Settings?.First(s => s.Id == "originMode").Value is not string originString
      || !OriginModeSetting.OriginModeMap.TryGetValue(originString, out OriginMode origin)
    )
    {
      return OriginMode.ModelOrigin; // Default to ModelOrigin if not specified or invalid
    }

    if (_originModeCache.TryGetValue(modelCard.ModelCardId.NotNull(), out OriginMode previousType))
    {
      if (previousType != origin)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _originModeCache[modelCard.ModelCardId.NotNull()] = origin;
    return origin;
  }

  public bool GetConvertHiddenElements(SenderModelCard modelCard)
  {
    if (modelCard == null)
    {
      throw new ArgumentNullException(nameof(modelCard));
    }

    var value = modelCard.Settings?.FirstOrDefault(s => s.Id == "convertHiddenElements")?.Value as bool?;

    var returnValue = value != null && value.NotNull();
    if (_convertHiddenElementsCache.TryGetValue(modelCard.ModelCardId.NotNull(), out var previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _convertHiddenElementsCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  public bool GetIncludeInternalProperties([NotNull] SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.FirstOrDefault(s => s.Id == "includeInternalProperties")?.Value as bool?;

    var returnValue = value != null && value.NotNull();
    if (_includeInternalPropertiesCache.TryGetValue(modelCard.ModelCardId.NotNull(), out var previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _includeInternalPropertiesCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  public bool GetPreserveModelHierarchy([NotNull] SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.FirstOrDefault(s => s.Id == "preserveModelHierarchy")?.Value as bool?;

    var returnValue = value != null && value.NotNull();
    if (_preserveModelHierarchyCache.TryGetValue(modelCard.ModelCardId.NotNull(), out var previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _preserveModelHierarchyCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    _sendConversionCache.EvictObjects(objectIds);
  }
}
