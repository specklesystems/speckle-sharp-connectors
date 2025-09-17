using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.RevitShared.Operations;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManager : IToSpeckleSettingsManager
{
  private readonly RevitContext _revitContext;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly ILogger<ToSpeckleSettingsManager> _logger;

  // cache invalidation process run with ModelCardId since the settings are model specific
  private readonly Dictionary<string, DetailLevelType> _detailLevelCache = [];
  private readonly Dictionary<string, Transform?> _referencePointCache = [];
  private readonly Dictionary<string, bool?> _sendNullParamsCache = [];
  private readonly Dictionary<string, bool?> _sendLinkedModelsCache = [];
  private readonly Dictionary<string, bool?> _sendRebarsAsVolumetricCache = [];

  public ToSpeckleSettingsManager(
    RevitContext revitContext,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker,
    ILogger<ToSpeckleSettingsManager> logger
  )
  {
    _revitContext = revitContext;
    _elementUnpacker = elementUnpacker;
    _sendConversionCache = sendConversionCache;
    _logger = logger;
  }

  public DetailLevelType GetDetailLevelSetting(SenderModelCard modelCard)
  {
    var fidelityString =
      modelCard.Settings?.FirstOrDefault(s => s.Id == RevitSettingsConstants.DETAIL_LEVEL)?.Value as string;
    if (
      fidelityString is not null
      && DetailLevelSetting.GeometryFidelityMap.TryGetValue(fidelityString, out DetailLevelType fidelity)
    )
    {
      if (_detailLevelCache.TryGetValue(modelCard.ModelCardId.NotNull(), out DetailLevelType previousType))
      {
        if (previousType != fidelity)
        {
          EvictCacheForModelCard(modelCard);
        }
      }
      _detailLevelCache[modelCard.ModelCardId.NotNull()] = fidelity;
      return fidelity;
    }

    // log the issue
    _logger.LogWarning(
      "Invalid detail level setting received: '{FidelityString}' for model {ModelCardId}. Using default: Medium",
      fidelityString,
      modelCard.ModelCardId
    );

    // return sensible default
    DetailLevelType defaultValue = RevitSettingsConstants.DEFAULT_DETAIL_LEVEL;
    _detailLevelCache[modelCard.ModelCardId.NotNull()] = defaultValue;
    return defaultValue;
  }

  public Transform? GetReferencePointSetting(ModelCard modelCard)
  {
    var referencePointString =
      modelCard.Settings?.FirstOrDefault(s => s.Id == RevitSettingsConstants.REFERENCE_POINT)?.Value as string;
    if (
      referencePointString is not null
      && ReferencePointSetting.ReferencePointMap.TryGetValue(
        referencePointString,
        out ReferencePointType referencePoint
      )
    )
    {
      // get the current transform from setting first
      // we are doing this because we can't track if reference points were changed between send operations.
      Transform? currentTransform = GetTransform(referencePoint);

      if (_referencePointCache.TryGetValue(modelCard.ModelCardId.NotNull(), out Transform? previousTransform))
      {
        // invalidate conversion cache if the transform has changed
        if (modelCard is SenderModelCard senderModelCard && previousTransform != currentTransform)
        {
          EvictCacheForModelCard(senderModelCard);
        }
      }

      _referencePointCache[modelCard.ModelCardId.NotNull()] = currentTransform;
      return currentTransform;
    }

    // log the issue
    _logger.LogWarning(
      "Invalid reference point setting received: '{ReferencePointString}' for model {ModelCardId}. Using default: InternalOrigin",
      referencePointString ?? "null",
      modelCard.ModelCardId
    );

    // return default (null for InternalOrigin means no transform)
    _referencePointCache[modelCard.ModelCardId.NotNull()] = null;
    return null;
  }

  public bool GetSendParameterNullOrEmptyStringsSetting(SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      RevitSettingsConstants.SEND_NULL_EMPTY_PARAMS,
      false,
      modelCard,
      _sendNullParamsCache,
      "Send null/empty parameters"
    );

  // NOTE: Cache invalidation currently a placeholder until we have more understanding on the sends
  // TODO: Evaluate cache invalidation for GetLinkedModelsSetting
  public bool GetLinkedModelsSetting(SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      RevitSettingsConstants.INCLUDE_LINKED_MODELS,
      true,
      modelCard,
      _sendLinkedModelsCache,
      "Linked models"
    );

  public bool GetSendRebarsAsVolumetric(SenderModelCard modelCard) =>
    GetBooleanSettingWithCache(
      RevitSettingsConstants.SEND_REBARS_AS_VOLUMETRIC,
      false,
      modelCard,
      _sendRebarsAsVolumetricCache,
      "Send rebars as volumetric"
    );

  /// <summary>
  /// Helper method to handle boolean settings with caching and logging
  /// </summary>
  private bool GetBooleanSettingWithCache(
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
        EvictCacheForModelCard(modelCard);
      }
    }

    cache[modelCard.ModelCardId] = returnValue;

    // NOTE: we probably don't need to log here BUT considering users might complain that a setting might not have been
    // respected (linked models disabled but still sent linked models), I think we should note this occurence so we know
    if (settingValue == null)
    {
      _logger.LogWarning(
        "{SettingName} setting was null for model {ModelCardId}, using default: {DefaultValue}",
        settingName,
        modelCard.ModelCardId,
        defaultValue
      );
    }

    return returnValue;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (doc == null)
    {
      throw new SpeckleException("Unable to retrieve active UI document");
    }
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds, doc);
    _sendConversionCache.EvictObjects(unpackedObjectIds);
  }

  private Transform? GetTransform(ReferencePointType referencePointType)
  {
    Transform? referencePointTransform = null;

    if (_revitContext.UIApplication is UIApplication uiApplication)
    {
      // first get the main doc base points and reference setting transform
      using FilteredElementCollector filteredElementCollector = new(uiApplication.ActiveUIDocument.Document);
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

    throw new InvalidOperationException(
      "Revit Context UI Application was null when retrieving reference point transform."
    );
  }
}
