using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Operations.Receive.Settings;

public class ToHostSettingsManager
{
  private readonly RevitContext _revitContext;

  public ToHostSettingsManager(RevitContext revitContext)
  {
    _revitContext = revitContext;
  }

  public Transform? GetReferencePointSetting(ReferencePointType referencePointType)
  {
    var referencePointString = referencePointType.ToString();
    if (
      ReferencePointSetting.ReferencePointMap.TryGetValue(referencePointString, out ReferencePointType referencePoint)
    )
    {
      Transform? currentTransform = GetTransform(referencePoint);
      return currentTransform;
    }

    throw new ArgumentException($"Invalid reference point value: {referencePointString}");
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
