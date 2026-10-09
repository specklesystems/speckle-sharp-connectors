using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public sealed class CsiFrameForceResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  public string ResultsKey => "frameForces";
  public ModelObjectType TargetObjectType => ModelObjectType.FRAME;

  // Grouped by frame OBJECT (+ object-relative station): ETABS auto-meshes a frame into several line elements, whose
  // names are unknown to the send selection and whose stations restart at 0 per element. Elm is kept as a level only
  // so the two coincident rows at a mesh boundary stay distinct.
  public ResultsConfiguration Configuration { get; } =
    new(["Obj", "Elm", "LoadCase", "Wrap:ObjSta", "Wrap:StepNum"], ["P", "V2", "V3", "T", "M2", "M3"]);

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
      throw new InvalidOperationException("Frame(s) are required in the selection for results extraction");
    }

    // Step 2: single dictionary to accumulate all results
    var allArrays = new Dictionary<string, List<object>>
    {
      ["Obj"] = [],
      ["ObjSta"] = [],
      ["Elm"] = [],
      ["LoadCase"] = [],
      ["StepNum"] = [],
      ["P"] = [],
      ["V2"] = [],
      ["V3"] = [],
      ["T"] = [],
      ["M2"] = [],
      ["M3"] = [],
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

      // accumulate results (bounded by numberResults - the API may hand back longer arrays than it filled)
      for (int i = 0; i < numberResults; i++)
      {
        bool objectOwned = !string.IsNullOrEmpty(obj[i]);
        allArrays["Obj"].Add(objectOwned ? obj[i] : elm[i]);
        allArrays["ObjSta"].Add(objectOwned ? objSta[i] : elmSta[i]);
        allArrays["Elm"].Add(elm[i]);
        allArrays["LoadCase"].Add(loadCase[i]);
        allArrays["StepNum"].Add(stepNum[i]);
        allArrays["P"].Add(p[i]);
        allArrays["V2"].Add(v2[i]);
        allArrays["V3"].Add(v3[i]);
        allArrays["T"].Add(t[i]);
        allArrays["M2"].Add(m2[i]);
        allArrays["M3"].Add(m3[i]);
      }
    }

    // Step 5: organise arrays for dictionary processor
    var rawArrays = allArrays.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value.ToArray());

    // Step 6: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
