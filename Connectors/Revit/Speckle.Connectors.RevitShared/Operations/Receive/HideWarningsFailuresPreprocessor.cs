using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// This class will suppress warnings on the Revit UI
/// Currently we use it after Revit receive when we create the group hierarchy
/// </summary>

public class HideWarningsFailuresPreprocessor : IFailuresPreprocessor
{
  public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
  {
    failuresAccessor.DeleteAllWarnings();
    return FailureProcessingResult.Continue;
  }
}
