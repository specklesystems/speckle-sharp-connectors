using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiJointReactResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  public string ResultsKey => "jointReact";
  public ModelObjectType TargetObjectType => ModelObjectType.JOINT;
  public ResultsConfiguration Configuration { get; } =
    new(["Elm", "LoadCase", "Wrap:StepNum"], ["F1", "F2", "F3", "M1", "M2", "M3"]);

  public CsiJointReactResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  public Dictionary<string, object> GetResults(IEnumerable<string>? objectNames = null)
  {
    // Step 1: validate input
    var jointNames = objectNames?.ToList();
    if (jointNames is null || jointNames.Count == 0)
    {
      throw new InvalidOperationException("Joint(s) are required in the selection for results extraction");
    }

    // Step 2: single dictionary to accumulate all results
    var allArrays = new Dictionary<string, List<object>>
    {
      ["Elm"] = [],
      ["LoadCase"] = [],
      ["StepNum"] = [],
      ["F1"] = [],
      ["F2"] = [],
      ["F3"] = [],
      ["M1"] = [],
      ["M2"] = [],
      ["M3"] = []
    };

    // Step 3: define api variables
    int numberResults = 0;
    string[] obj = [],
      elm = [],
      loadCase = [],
      stepType = [];
    double[] stepNum = [],
      f1 = [],
      f2 = [],
      f3 = [],
      m1 = [],
      m2 = [],
      m3 = [];

    // Step 4: iterate through objectNames and get joint reaction results for those that are assigned restraints / springs and grounded
    foreach (string jointName in jointNames)
    {
      // this only works if the joint has restraints or springs assignments, so check if it's a valid query first
      bool[] restraints = [];
      string springAssignment = string.Empty;
      _settingsStore.Current.SapModel.PointObj.GetRestraint(jointName, ref restraints);
      _settingsStore.Current.SapModel.PointObj.GetSpringAssignment(jointName, ref springAssignment);
      if (restraints.All(r => !r) && string.IsNullOrEmpty(springAssignment))
      {
        continue; // skip this joint - it has neither restraints nor springs
      }

      int success = _settingsStore.Current.SapModel.Results.JointReact(
        jointName,
        eItemTypeElm.ObjectElm,
        ref numberResults,
        ref obj,
        ref elm,
        ref loadCase,
        ref stepType,
        ref stepNum,
        ref f1,
        ref f2,
        ref f3,
        ref m1,
        ref m2,
        ref m3
      );

      if (success != 0)
      {
        throw new InvalidOperationException($"Joint force extraction failed for frame {jointName}."); // shouldn't fail silently
      }

      // accumulate results
      allArrays["Elm"].AddRange(elm.Cast<object>());
      allArrays["LoadCase"].AddRange(loadCase.Cast<object>());
      allArrays["StepNum"].AddRange(stepNum.Cast<object>());
      allArrays["F1"].AddRange(f1.Cast<object>());
      allArrays["F2"].AddRange(f2.Cast<object>());
      allArrays["F3"].AddRange(f3.Cast<object>());
      allArrays["M1"].AddRange(m1.Cast<object>());
      allArrays["M2"].AddRange(m2.Cast<object>());
      allArrays["M3"].AddRange(m3.Cast<object>());
    }

    // Step 5: organise arrays for dictionary processor
    var rawArrays = allArrays.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value.ToArray());

    // Step 6: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
