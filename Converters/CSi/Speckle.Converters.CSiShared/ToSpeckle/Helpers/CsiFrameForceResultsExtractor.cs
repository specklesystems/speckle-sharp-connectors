using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public sealed class CsiFrameForceResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  public string ResultsKey { get; } = "FrameForces";

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
    // Step 1: define arrays that the results processor will work on
    var allElm = new List<string>();
    var allElmSta = new List<double>();
    var allLoadCase = new List<string>();
    var allStepNum = new List<double>();
    var allP = new List<double>();
    var allV2 = new List<double>();
    var allV3 = new List<double>();
    var allT = new List<double>();
    var allM2 = new List<double>();
    var allM3 = new List<double>();

    // Step 2: define variables for api calls to populate
    int numberResults = 0;
    string[] obj = [];
    double[] objSta = [];
    string[] elm = [];
    double[] elmSta = [];
    string[] loadCase = [];
    string[] stepType = [];
    double[] stepNum = [];
    double[] p = [];
    double[] v2 = [];
    double[] v3 = [];
    double[] t = [];
    double[] m2 = [];
    double[] m3 = [];

    // Step 3: this extractor (and method) MUST have objectNames argument
    if (objectNames is null)
    {
      throw new InvalidOperationException(
        "This operation relies on objectNames to extract results for selected frames only."
      );
    }

    // Step 4: iterate through objectNames and get frame results for those
    foreach (string frameName in objectNames)
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

      // frame arrays to be added to the "bigger" arrays
      allElm.AddRange(elm);
      allElmSta.AddRange(elmSta);
      allLoadCase.AddRange(loadCase);
      allStepNum.AddRange(stepNum);
      allP.AddRange(p);
      allV2.AddRange(v2);
      allV3.AddRange(v3);
      allT.AddRange(t);
      allM2.AddRange(m2);
      allM3.AddRange(m3);
    }

    // Step 5: organise arrays for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["Elm"] = allElm.ToArray(),
      ["ElmSta"] = allElmSta.ToArray(),
      ["LoadCase"] = allLoadCase.ToArray(),
      ["StepNum"] = allStepNum.ToArray(),
      ["P"] = allP.ToArray(),
      ["V2"] = allV2.ToArray(),
      ["V3"] = allV3.ToArray(),
      ["T"] = allT.ToArray(),
      ["M2"] = allM2.ToArray(),
      ["M3"] = allM3.ToArray()
    };

    // Step 6: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
