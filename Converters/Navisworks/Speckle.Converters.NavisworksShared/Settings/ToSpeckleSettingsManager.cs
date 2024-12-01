using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converter.Navisworks.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connector.Navisworks.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManager(ISendConversionCache sendConversionCache) : IToSpeckleSettingsManager
{
  private readonly Dictionary<string, Dictionary<string, object?>> _settingsCache = [];

  public RepresentationMode GetVisualRepresentationMode(SenderModelCard modelCard)
  {
    var value = GetSettingValue<string>(modelCard, "visualRepresentation") ?? "Active";
    var mode = value switch
    {
      "Active" => RepresentationMode.ACTIVE,
      "Original" => RepresentationMode.ORIGINAL,
      "Permanent" => RepresentationMode.PERMANENT,
      _ => RepresentationMode.ACTIVE
    };

    UpdateSettingCache(modelCard, "visualRepresentation", mode);
    return mode;
  }

  public OriginMode GetOriginMode(SenderModelCard modelCard)
  {
    var value = GetSettingValue<string>(modelCard, "originMode") ?? "ModelOrigin";
    var mode = value switch
    {
      "ModelOrigin" => OriginMode.MODELORIGIN,
      "ProjectBaseOrigin" => OriginMode.PROJECTBASEORIGIN,
      "BoundingBoxOrigin" => OriginMode.BOUNDINGBOXORIGIN,
      _ => OriginMode.MODELORIGIN
    };

    UpdateSettingCache(modelCard, "originMode", mode);
    return mode;
  }

  public bool GetConvertHiddenElements(SenderModelCard modelCard)
  {
    var value = GetSettingValue<bool>(modelCard, "convertHiddenElements");
    UpdateSettingCache(modelCard, "convertHiddenElements", value);
    return value;
  }

  public bool GetIncludeInternalProperties(SenderModelCard modelCard)
  {
    var value = GetSettingValue<bool>(modelCard, "includeInternalProperties");
    UpdateSettingCache(modelCard, "includeInternalProperties", value);
    return value;
  }

  private static T? GetSettingValue<T>(SenderModelCard modelCard, string settingId) =>
    modelCard.Settings?.FirstOrDefault(s => s.Id == settingId)?.Value is T value ? value : default;

  private void UpdateSettingCache(SenderModelCard modelCard, string settingId, object? newValue)
  {
    var modelId = modelCard.ModelCardId.NotNull();

    if (!_settingsCache.TryGetValue(modelId, out var settings))
    {
      settings = [];
      _settingsCache[modelId] = settings;
    }

    if (settings.TryGetValue(settingId, out var oldValue) && Equals(oldValue, newValue))
    {
      return;
    }

    settings[settingId] = newValue;

    // If setting changed, invalidate conversion cache for this model's objects
    var objectIds = modelCard.SendFilter?.SelectedObjectIds ?? [];
    sendConversionCache.EvictObjects(objectIds);
  }
}
