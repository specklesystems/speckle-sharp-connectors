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

  private Transform? GetTransform(ReceiveReferencePointType referencePointType)
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
        case ReceiveReferencePointType.ProjectBase:
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
        case ReceiveReferencePointType.Survey:
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

        case ReceiveReferencePointType.Source:
          break;
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
