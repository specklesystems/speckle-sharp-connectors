using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManager : IToSpeckleSettingsManager
{
  private readonly RevitContext _revitContext;
  private readonly APIContext _apiContext;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;

  // cache invalidation process run with ModelCardId since the settings are model specific
  private readonly Dictionary<string, DetailLevelType> _detailLevelCache = new();
  private readonly Dictionary<string, Transform?> _referencePointCache = new();
  private readonly Dictionary<string, bool?> _sendNullParamsCache = new();

  public ToSpeckleSettingsManager(
    RevitContext revitContext,
    APIContext apiContext,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker
  )
  {
    _revitContext = revitContext;
    _apiContext = apiContext;
    _elementUnpacker = elementUnpacker;
    _sendConversionCache = sendConversionCache;
  }

  public async Task<DetailLevelType> GetDetailLevelSetting(SenderModelCard modelCard)
  {
    var fidelityString = modelCard.Settings?.First(s => s.Id == "detailLevel").Value as string;
    if (
      fidelityString is not null
      && DetailLevelSetting.GeometryFidelityMap.TryGetValue(fidelityString, out DetailLevelType fidelity)
    )
    {
      if (_detailLevelCache.TryGetValue(modelCard.ModelCardId.NotNull(), out DetailLevelType previousType))
      {
        if (previousType != fidelity)
        {
          await EvictCacheForModelCard(modelCard).ConfigureAwait(false);
        }
      }
      _detailLevelCache[modelCard.ModelCardId.NotNull()] = fidelity;
      return fidelity;
    }

    throw new ArgumentException($"Invalid geometry fidelity value: {fidelityString}");
  }

  public async Task<Transform?> GetReferencePointSetting(SenderModelCard modelCard)
  {
    var referencePointString = modelCard.Settings?.First(s => s.Id == "referencePoint").Value as string;
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
      Transform? currentTransform = GetTransform(_revitContext, referencePoint);

      if (_referencePointCache.TryGetValue(modelCard.ModelCardId.NotNull(), out Transform? previousTransform))
      {
        // invalidate conversion cache if the transform has changed
        if (previousTransform != currentTransform)
        {
          await EvictCacheForModelCard(modelCard).ConfigureAwait(false);
        }
      }

      _referencePointCache[modelCard.ModelCardId.NotNull()] = currentTransform;
      return currentTransform;
    }

    throw new ArgumentException($"Invalid reference point value: {referencePointString}");
  }

  public async Task<bool> GetSendParameterNullOrEmptyStringsSetting(SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.First(s => s.Id == "nullemptyparams").Value as bool?;
    var returnValue = value != null && value.NotNull();
    if (_sendNullParamsCache.TryGetValue(modelCard.ModelCardId.NotNull(), out bool? previousValue))
    {
      if (previousValue != returnValue)
      {
        await EvictCacheForModelCard(modelCard).ConfigureAwait(false);
      }
    }

    _sendNullParamsCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private async Task EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds =
      modelCard.SendFilter != null
        ? await _apiContext.Run(_ => modelCard.SendFilter.NotNull().GetObjectIds()).ConfigureAwait(false)
        : [];
    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds);
    _sendConversionCache.EvictObjects(unpackedObjectIds);
  }

  private Transform? GetTransform(RevitContext context, ReferencePointType referencePointType)
  {
    Transform? referencePointTransform = null;

    if (context.UIApplication is UIApplication uiApplication)
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

        default:
          break;
      }

      return referencePointTransform;
    }

    throw new InvalidOperationException(
      "Revit Context UI Application was null when retrieving reference point transform."
    );
  }
}
