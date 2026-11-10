using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Revit.Operations.Receive.Settings;

[GenerateAutoInterface]
public class ToHostSettingsManager : IToHostSettingsManager
{
  private readonly RevitContext _revitContext;
  private readonly ILogger<ToHostSettingsManager> _logger;

  public ToHostSettingsManager(RevitContext revitContext, ILogger<ToHostSettingsManager> logger)
  {
    _revitContext = revitContext;
    _logger = logger;
  }

  public Transform? GetReferencePointSetting(ModelCard modelCard)
  {
    var referencePointString =
      modelCard.Settings?.FirstOrDefault(s => s.Id == ReceiveReferencePointSetting.SETTING_ID)?.Value as string;
    if (
      referencePointString is not null
      && ReceiveReferencePointSetting.ReferencePointMap.TryGetValue(
        referencePointString,
        out ReceiveReferencePointType referencePoint
      )
    )
    {
      // get the current transform from setting first
      // we are doing this because we can't track if reference points were changed between send operations.
      Transform? currentTransform = GetTransform(referencePoint);
      return currentTransform;
    }

    // log the issue
    _logger.LogWarning(
      "Invalid reference point setting received: '{ReferencePointString}' for model {ModelCardId}, using default: {DefaultValue}",
      referencePointString,
      modelCard.ModelCardId,
      ReceiveReferencePointSetting.DEFAULT_VALUE
    );

    // return default (null for Source means no transform)
    return null;
  }

  public bool GetReceiveInstancesAsFamiliesSetting(ModelCard modelCard) =>
    GetBoolSetting(
      modelCard,
      ReceiveInstancesAsFamiliesSetting.SETTING_ID,
      ReceiveInstancesAsFamiliesSetting.DEFAULT_VALUE,
      "Receive instances as families"
    );

  public bool GetReceivePropertiesAsParametersSetting(ModelCard modelCard) =>
    GetBoolSetting(
      modelCard,
      ReceivePropertiesAsParametersSetting.SETTING_ID,
      ReceivePropertiesAsParametersSetting.DEFAULT_VALUE,
      "Receive properties as parameters"
    );

  private bool GetBoolSetting(ModelCard modelCard, string settingId, bool defaultValue, string label)
  {
    var settingValue = modelCard.Settings?.FirstOrDefault(s => s.Id == settingId)?.Value as bool?;

    if (settingValue is not null)
    {
      return settingValue.Value;
    }

    _logger.LogWarning(
      "{Setting} setting was null for model {ModelCardId}, using default: {DefaultValue}",
      label,
      modelCard.ModelCardId,
      defaultValue
    );

    return defaultValue;
  }

  private Transform? GetTransform(ReceiveReferencePointType referencePointType)
  {
    Transform? referencePointTransform = null;

    if (_revitContext.UIApplication is UIApplication uiApplication)
    {
      // first get the main doc base points
      using FilteredElementCollector filteredElementCollector = new(uiApplication.ActiveUIDocument.Document);
      var points = filteredElementCollector.OfClass(typeof(BasePoint)).Cast<BasePoint>().ToList();
      BasePoint? projectPoint = points.FirstOrDefault(o => !o.IsShared);
      BasePoint? surveyPoint = points.FirstOrDefault(o => o.IsShared);

      switch (referencePointType)
      {
        case ReceiveReferencePointType.ProjectBase:
          referencePointTransform = projectPoint is not null
            ? Transform.CreateTranslation(projectPoint.Position)
            : throw new InvalidOperationException("Couldn't retrieve Project Point from document");
          break;

        case ReceiveReferencePointType.Survey:
          referencePointTransform = surveyPoint is not null
            ? Transform.CreateTranslation(surveyPoint.Position)
            : throw new InvalidOperationException("Couldn't retrieve Survey Point from document");
          break;

        case ReceiveReferencePointType.SharedCoordinates:
          referencePointTransform =
            uiApplication.ActiveUIDocument.Document.ActiveProjectLocation?.GetTotalTransform()
            ?? throw new InvalidOperationException("Couldn't retrieve Shared Coordinates transform from document");
          break;

        case ReceiveReferencePointType.Source:
        case ReceiveReferencePointType.InternalOrigin:
          break;
      }

      return referencePointTransform;
    }

    throw new InvalidOperationException(
      "Revit Context UI Application was null when retrieving reference point transform"
    );
  }
}
