using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class HideWarningsFailuresPreprocessor : IFailuresPreprocessor
{
  public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
  {
    var failList = failuresAccessor.GetFailureMessages();
    foreach (FailureMessageAccessor failure in failList)
    {
      var t = failure.GetDescriptionText();
      var r = failure.GetDefaultResolutionCaption();
      // TODO do something with the message
      Console.WriteLine($"{r}, {t}");

      //Globals.ConversionErrors.Add(new SpeckleError { Message = t });
    }

    failuresAccessor.DeleteAllWarnings();
    return FailureProcessingResult.Continue;
  }
}
