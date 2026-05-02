using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.RevitShared.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManager(
  ISendConversionCache sendConversionCache,
  ElementUnpacker elementUnpacker,
  ILogger<ToSpeckleSettingsManager> logger
) : IToSpeckleSettingsManager
{
  // cache invalidation process run with ModelCardId since the settings are model specific
  private readonly Dictionary<string, DetailLevelType> _detailLevelCache = [];
  private readonly Dictionary<string, Transform?> _referencePointCache = [];
  private readonly Dictionary<string, bool?> _sendNullParamsCache = [];
  private readonly Dictionary<string, bool?> _sendLinkedModelsCache = [];
  private readonly Dictionary<string, bool?> _sendRebarsAsVolumetricCache = [];
  private readonly Dictionary<string, bool?> _sendAreasAsMeshCache = [];
  private readonly Dictionary<string, AppendRoomsAndAreasMode> _appendRoomsAndAreasCache = [];

  public DetailLevelType GetDetailLevelSetting(Document document, SenderModelCard modelCard) =>
    GetEnumSettingWithCache(
      document,
      DetailLevelSetting.SETTING_ID,
      DetailLevelSetting.GeometryFidelityMap,
      DetailLevelSetting.DEFAULT_VALUE,
      modelCard,
      _detailLevelCache,
      "detail level"
    );

  public Transform? GetReferencePointSetting(Document document, ModelCard modelCard)
  {
    var referencePointString =
      modelCard.Settings?.FirstOrDefault(s => s.Id == SendReferencePointSetting.SETTING_ID)?.Value as string;
    if (
      referencePointString is not null
      && SendReferencePointSetting.ReferencePointMap.TryGetValue(
        referencePointString,
        out ReferencePointType referencePoint
      )
    )
    {
      // get the current transform from setting first
      // we are doing this because we can't track if reference points were changed between send operations.
      Transform? currentTransform = GetTransform(document, referencePoint);

      if (_referencePointCache.TryGetValue(modelCard.ModelCardId.NotNull(), out Transform? previousTransform))
      {
        // invalidate conversion cache if the transform has changed
        if (modelCard is SenderModelCard senderModelCard && previousTransform != currentTransform)
        {
          EvictCacheForModelCard(document, senderModelCard);
        }
      }

      _referencePointCache[modelCard.ModelCardId.NotNull()] = currentTransform;
      return currentTransform;
    }

    // log the issue
    logger.LogWarning(
      "Invalid reference point setting received: '{ReferencePointString}' for model {ModelCardId}, using default: {DefaultValue}",
      referencePointString,
      modelCard.ModelCardId,
      SendReferencePointSetting.DEFAULT_VALUE
    );

    // return default (null for InternalOrigin means no transform)
    _referencePointCache[modelCard.ModelCardId.NotNull()] = null;
    return null;
  }

  public bool GetSendParameterNullOrEmptyStringsSetting(Document document, SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      document,
      SendParameterNullOrEmptyStringsSetting.SETTING_ID,
      SendParameterNullOrEmptyStringsSetting.DEFAULT_VALUE,
      modelCard,
      _sendNullParamsCache,
      "Send null/empty parameters"
    );

  // NOTE: Cache invalidation currently a placeholder until we have more understanding on the sends
  // TODO: Evaluate cache invalidation for GetLinkedModelsSetting
  public bool GetLinkedModelsSetting(Document document, SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      document,
      LinkedModelsSetting.SETTING_ID,
      LinkedModelsSetting.DEFAULT_VALUE,
      modelCard,
      _sendLinkedModelsCache,
      "Linked models"
    );

  public bool GetSendRebarsAsVolumetric(Document document, SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      document,
      SendRebarsAsVolumetricSetting.SETTING_ID,
      SendRebarsAsVolumetricSetting.DEFAULT_VALUE,
      modelCard,
      _sendRebarsAsVolumetricCache,
      "Send rebars as volumetric"
    );

  public bool GetSendAreasAsMesh(Document document, SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      document,
      SendAreasAsMeshSetting.SETTING_ID,
      SendAreasAsMeshSetting.DEFAULT_VALUE,
      modelCard,
      _sendAreasAsMeshCache,
      "Send areas as mesh"
    );

  public AppendRoomsAndAreasMode GetAppendRoomsAndAreas(Document document, SenderModelCard modelCard) =>
    GetEnumSettingWithCache(
      document,
      AppendRoomsAndAreasSetting.SETTING_ID,
      AppendRoomsAndAreasSetting.AppendRoomsAndAreasMap,
      AppendRoomsAndAreasMode.None,
      modelCard,
      _appendRoomsAndAreasCache,
      "appendRoomsAndAreas"
    );

  /// <summary>
  /// Collects rooms and/or areas from the document per the card setting, excluding elements already present in <paramref name="existingIds"/>.
  /// </summary>
  public IReadOnlyList<Element> GetElementsToAppend(
    Document document,
    SenderModelCard modelCard,
    HashSet<string> existingIds
  )
  {
    var mode = GetAppendRoomsAndAreas(document, modelCard);
    if (mode == AppendRoomsAndAreasMode.None)
    {
      return [];
    }

    var toAppend = new List<Element>();

    if (mode is AppendRoomsAndAreasMode.RoomsOnly or AppendRoomsAndAreasMode.Both)
    {
      using var roomCollector = new FilteredElementCollector(document);
      toAppend.AddRange(
        roomCollector
          .OfClass(typeof(SpatialElement))
          .OfCategory(BuiltInCategory.OST_Rooms)
          .Cast<Element>()
          .Where(e => !existingIds.Contains(e.UniqueId))
      );
    }

    if (mode is AppendRoomsAndAreasMode.AreasOnly or AppendRoomsAndAreasMode.Both)
    {
      using var areaCollector = new FilteredElementCollector(document);
      toAppend.AddRange(
        areaCollector
          .OfClass(typeof(SpatialElement))
          .OfCategory(BuiltInCategory.OST_Areas)
          .Cast<Element>()
          .Where(e => !existingIds.Contains(e.UniqueId))
      );
    }

    return toAppend;
  }

  /// <summary>
  /// Helper method to handle enum settings with string-keyed maps, per-card caching, and cache eviction on change.
  /// </summary>
  private TEnum GetEnumSettingWithCache<TEnum>(
    Document document,
    string settingId,
    Dictionary<string, TEnum> map,
    TEnum defaultValue,
    SenderModelCard modelCard,
    Dictionary<string, TEnum> cache,
    string settingName
  )
    where TEnum : struct
  {
    var valueString = modelCard.Settings?.FirstOrDefault(s => s.Id == settingId)?.Value as string;
    if (valueString is not null && map.TryGetValue(valueString, out TEnum value))
    {
      if (
        cache.TryGetValue(modelCard.ModelCardId.NotNull(), out TEnum previous)
        && !EqualityComparer<TEnum>.Default.Equals(previous, value)
      )
      {
        EvictCacheForModelCard(document, modelCard);
      }
      cache[modelCard.ModelCardId.NotNull()] = value;
      return value;
    }

    logger.LogWarning(
      "Invalid {SettingName} setting received: '{ValueString}' for model {ModelCardId}, using default: {DefaultValue}",
      settingName,
      valueString,
      modelCard.ModelCardId,
      defaultValue
    );
    cache[modelCard.ModelCardId.NotNull()] = defaultValue;
    return defaultValue;
  }

  /// <summary>
  /// Helper method to handle boolean settings with caching and logging
  /// </summary>
  private bool GetBooleanSettingWithCache(
    Document document,
    string settingId,
    bool defaultValue,
    SenderModelCard modelCard,
    Dictionary<string, bool?> cache,
    string settingName
  )
  {
    var settingValue = modelCard.Settings?.FirstOrDefault(s => s.Id == settingId)?.Value as bool?;
    bool returnValue = settingValue ?? defaultValue;

    if (cache.TryGetValue(modelCard.ModelCardId.NotNull(), out bool? previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(document, modelCard);
      }
    }

    cache[modelCard.ModelCardId] = returnValue;

    // NOTE: we probably don't need to log here BUT considering users might complain that a setting might not have been
    // respected (linked models disabled but still sent linked models), I think we should note this occurence so we know
    if (settingValue == null)
    {
      logger.LogWarning(
        "{SettingName} setting was null for model {ModelCardId}, using default: {DefaultValue}",
        settingName,
        modelCard.ModelCardId,
        defaultValue
      );
    }

    return returnValue;
  }

  private void EvictCacheForModelCard(Document document, SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter?.SelectedObjectIds ?? [];
    var unpackedObjectIds = elementUnpacker.GetUnpackedElementIds(objectIds, document);
    sendConversionCache.EvictObjects(unpackedObjectIds);
  }

  private Transform? GetTransform(Document document, ReferencePointType referencePointType)
  {
    Transform? referencePointTransform = null;

    // first get the main doc base points and reference setting transform
    using FilteredElementCollector filteredElementCollector = new(document);
    var points = filteredElementCollector.OfClass(typeof(BasePoint)).Cast<BasePoint>().ToList();
    BasePoint? projectPoint = points.FirstOrDefault(o => !o.IsShared);
    BasePoint? surveyPoint = points.FirstOrDefault(o => o.IsShared);

    switch (referencePointType)
    {
      case ReferencePointType.ProjectBase:
        referencePointTransform = projectPoint is not null
          ? Transform.CreateTranslation(projectPoint.Position)
          : throw new InvalidOperationException("Couldn't retrieve Project Point from document");
        break;

      case ReferencePointType.Survey:
        referencePointTransform = surveyPoint is not null
          ? Transform.CreateTranslation(surveyPoint.Position)
          : throw new InvalidOperationException("Couldn't retrieve Survey Point from document");
        break;

      case ReferencePointType.SharedCoordinates:
        referencePointTransform =
          document.ActiveProjectLocation?.GetTotalTransform()
          ?? throw new InvalidOperationException("Couldn't retrieve Shared Coordinates transform from document");
        break;

      case ReferencePointType.InternalOrigin:
        break;
    }

    return referencePointTransform;
  }
}
