using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// POC: reference point functionality needs to be revisited (we are currently baking in these transforms into all geometry using the point and vector converters, and losing the transform).
/// This converter stores the selected reference point setting value and provides a method to get out the transform for this reference point.
/// POC: uses context stack current doc on instantiation and assumes context stack doc won't change. Linked docs will NOT be supported atm.
/// </summary>
public class ReferencePointConverter : IReferencePointConverter
{
  private const string REFPOINT_INTERNAL_ORIGIN = "Internal Origin (default)";
  private const string REFPOINT_PROJECT_BASE = "Project Base";
  private const string REFPOINT_SURVEY = "Survey";

  private readonly RevitConversionSettings _revitSettings;
  private readonly IRevitConversionContextStack _contextStack;

  private DB.Transform? Transform { get; }

  public ReferencePointConverter(RevitConversionSettings revitSettings, IRevitConversionContextStack contextStack)
  {
    _revitSettings = revitSettings;
    _contextStack = contextStack;
    Transform = GetReferencePointTransform();
  }

  public DB.XYZ ConvertToExternalCoordinates(DB.XYZ p, bool isPoint)
  {
    if (Transform is DB.Transform transform)
    {
      return isPoint ? transform.Inverse.OfPoint(p) : transform.Inverse.OfVector(p);
    }

    return p;
  }

  public DB.XYZ ConvertToInternalCoordinates(DB.XYZ p, bool isPoint)
  {
    if (Transform is DB.Transform transform)
    {
      return isPoint ? transform.OfPoint(p) : transform.OfVector(p);
    }

    return p;
  }

  private DB.Transform? GetReferencePointTransform()
  {
    string referencePointSetting = _revitSettings.TryGetSettingString("reference-point", out string? value)
      ? value.NotNull()
      : string.Empty;

    DB.Transform? referencePointTransform = null;

    // first get the main doc base points and reference setting transform
    using DB.FilteredElementCollector filteredElementCollector = new(_contextStack.Current.Document);
    var points = filteredElementCollector.OfClass(typeof(DB.BasePoint)).Cast<DB.BasePoint>().ToList();
    DB.BasePoint? projectPoint = points.FirstOrDefault(o => !o.IsShared);
    DB.BasePoint? surveyPoint = points.FirstOrDefault(o => o.IsShared);

    switch (referencePointSetting)
    {
      // note that the project base (ui) rotation is registered on the survey pt, not on the base point
      case REFPOINT_PROJECT_BASE:
        if (projectPoint is not null)
        {
          referencePointTransform = DB.Transform.CreateTranslation(projectPoint.Position);
          // TODO: return error, couldn't retrieve project point
        }
        break;

      case REFPOINT_SURVEY:
        // note that the project base (ui) rotation is registered on the survey pt, not on the base point
        // retrieve the survey point rotation from the project point
        if (surveyPoint is not null)
        {
          // POC: should a null angle resolve to 0?
          var angle = surveyPoint.get_Parameter(DB.BuiltInParameter.BASEPOINT_ANGLETON_PARAM)?.AsDouble() ?? 0;

          // POC: following disposed incorrectly or early or maybe a false negative?
          using DB.Transform translation = DB.Transform.CreateTranslation(surveyPoint.Position);
          referencePointTransform = translation.Multiply(DB.Transform.CreateRotation(DB.XYZ.BasisZ, angle));
        }
        break;

      case REFPOINT_INTERNAL_ORIGIN:
        break;

      default:
        break;
    }

    return referencePointTransform;
  }
}
