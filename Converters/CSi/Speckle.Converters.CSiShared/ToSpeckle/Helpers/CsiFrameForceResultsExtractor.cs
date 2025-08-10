using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public sealed class CsiFrameForceResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  public string ResultsKey => "FrameForces";
  public ModelObjectType TargetObjectType => ModelObjectType.FRAME;

  public ResultsConfiguration Configuration { get; } =
    new(["Elm", "LoadCase", "Wrap:ElmSta", "Wrap:StepNum"], ["P", "V2", "V3", "T", "M2", "M3"]);

  public CsiFrameForceResultsExtractor(
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
    var frameNames = objectNames?.ToList();
    if (frameNames is null || frameNames.Count == 0)
    {
      throw new InvalidOperationException("Frame names are required for force extraction");
    }

    // Step 2: single dictionary to accumulate all results
    var allArrays = new Dictionary<string, List<object>>
    {
      ["Elm"] = new(),
      ["ElmSta"] = new(),
      ["LoadCase"] = new(),
      ["StepNum"] = new(),
      ["P"] = new(),
      ["V2"] = new(),
      ["V3"] = new(),
      ["T"] = new(),
      ["M2"] = new(),
      ["M3"] = new()
    };

    // Step 3: define api variables
    int numberResults = 0;
    string[] obj = [],
      elm = [],
      loadCase = [],
      stepType = [];
    double[] objSta = [],
      elmSta = [],
      stepNum = [],
      p = [],
      v2 = [],
      v3 = [],
      t = [],
      m2 = [],
      m3 = [];

    // Step 4: iterate through objectNames and get frame results for those
    foreach (string frameName in frameNames)
    {
      int success = _settingsStore.Current.SapModel.Results.FrameForce(
        frameName,
        eItemTypeElm.ObjectElm,
        ref numberResults,
        ref obj,
        ref objSta,
        ref elm,
        ref elmSta,
        ref loadCase,
        ref stepType,
        ref stepNum,
        ref p,
        ref v2,
        ref v3,
        ref t,
        ref m2,
        ref m3
      );

      if (success != 0)
      {
        throw new InvalidOperationException($"Frame force extraction failed for frame {frameName}."); // shouldn't fail silently
      }

      // accumulate results
      allArrays["Elm"].AddRange(elm.Cast<object>());
      allArrays["ElmSta"].AddRange(elmSta.Cast<object>());
      allArrays["LoadCase"].AddRange(loadCase.Cast<object>());
      allArrays["StepNum"].AddRange(stepNum.Cast<object>());
      allArrays["P"].AddRange(p.Cast<object>());
      allArrays["V2"].AddRange(v2.Cast<object>());
      allArrays["V3"].AddRange(v3.Cast<object>());
      allArrays["T"].AddRange(t.Cast<object>());
      allArrays["M2"].AddRange(m2.Cast<object>());
      allArrays["M3"].AddRange(m3.Cast<object>());
    }

    // Step 5: organise arrays for dictionary processor
    var rawArrays = allArrays.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value.ToArray());

    // Step 6: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
