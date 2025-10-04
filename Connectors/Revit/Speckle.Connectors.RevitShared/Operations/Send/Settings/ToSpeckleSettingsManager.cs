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
  private readonly Dictionary<string, bool?> _sendMaterialCustomParametersCache = [];

  public DetailLevelType GetDetailLevelSetting(Document document, SenderModelCard modelCard)
  {
    var fidelityString =
      modelCard.Settings?.FirstOrDefault(s => s.Id == DetailLevelSetting.SETTING_ID)?.Value as string;
    if (
      fidelityString is not null
      && DetailLevelSetting.GeometryFidelityMap.TryGetValue(fidelityString, out DetailLevelType fidelity)
    )
    {
      if (_detailLevelCache.TryGetValue(modelCard.ModelCardId.NotNull(), out DetailLevelType previousType))
      {
        if (previousType != fidelity)
        {
          EvictCacheForModelCard(document, modelCard);
        }
      }
      _detailLevelCache[modelCard.ModelCardId.NotNull()] = fidelity;
      return fidelity;
    }

    // log the issue
    logger.LogWarning(
      "Invalid detail level setting received: '{FidelityString}' for model {ModelCardId}, using default: {DefaultValue}",
      fidelityString,
      modelCard.ModelCardId,
      DetailLevelSetting.DEFAULT_VALUE
    );

    // return sensible default
    DetailLevelType defaultValue = DetailLevelSetting.DEFAULT_VALUE;
    _detailLevelCache[modelCard.ModelCardId.NotNull()] = defaultValue;
    return defaultValue;
  }

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

  public bool GetSendMaterialCustomParameters(Document document, SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      document,
      SendMaterialCustomParameters.SETTING_ID,
      SendMaterialCustomParameters.DEFAULT_VALUE,
      modelCard,
      _sendMaterialCustomParametersCache,
      "Send material custom parameters"
    );

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
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
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
      // note that the project base (ui) rotation is registered on the survey pt, not on the base point
      case ReferencePointType.ProjectBase:
        if (projectPoint is not null)
        {
          referencePointTransform = Transform.CreateTranslation(projectPoint.Position);
        }
        else
        {
          throw new InvalidOperationException("Couldn't retrieve Project Point from document");
        }
        break;

      // note that the project base (ui) rotation is registered on the survey pt, not on the base point
      case ReferencePointType.Survey:
        if (surveyPoint is not null && projectPoint is not null)
        {
          // POC: should a null angle resolve to 0?
          // retrieve the survey point rotation from the project point
          var angle = projectPoint.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM)?.AsDouble() ?? 0;

          // POC: following disposed incorrectly or early or maybe a false negative?
          using Transform translation = Transform.CreateTranslation(surveyPoint.Position);
          referencePointTransform = translation.Multiply(Transform.CreateRotation(XYZ.BasisZ, angle));
        }
        else
        {
          throw new InvalidOperationException("Couldn't retrieve Survey and Project Point from document");
        }
        break;

      case ReferencePointType.InternalOrigin:
        break;
    }

    return referencePointTransform;
  }
}
