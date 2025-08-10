using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiBaseReactResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  public string ResultsKey => "baseReact";
  public ModelObjectType TargetObjectType => ModelObjectType.JOINT;
  public ResultsConfiguration Configuration { get; } =
    new(["LoadCase", "Wrap:StepNum"], ["FX", "FY", "FZ", "MX", "ParamMy", "MZ"]);

  public CsiBaseReactResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  // NOTE: since these are global reactions, they're independent of the user selection, therefore discarded
  public Dictionary<string, object> GetResults(IEnumerable<string>? _)
  {
    // Step 1: define api variables
    int numberResults = 0;
    string[] loadCase = [],
      stepType = [];
    double[] stepNum = [],
      fx = [],
      fy = [],
      fz = [],
      mx = [],
      paramMy = [],
      mz = [];
    double gx = 0,
      gy = 0,
      gz = 0;

    // Step 2: api call
    int success = _settingsStore.Current.SapModel.Results.BaseReact(
      ref numberResults,
      ref loadCase,
      ref stepType,
      ref stepNum,
      ref fx,
      ref fy,
      ref fz,
      ref mx,
      ref paramMy,
      ref mz,
      ref gx,
      ref gy,
      ref gz
    );

    if (success != 0 || numberResults == 0)
    {
      throw new InvalidOperationException("Base reaction extraction failed."); // shouldn't fail silently
    }

    // Step 3: organise arrays for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["LoadCase"] = loadCase,
      ["StepNum"] = stepNum,
      ["FX"] = fx,
      ["FY"] = fy,
      ["FZ"] = fz,
      ["MX"] = mx,
      ["ParamMy"] = paramMy,
      ["MZ"] = mz
    };

    // Step 4: return sorted and processed dictionary
    var resultsDictionary = _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);

    // Step 5: add the extra centroid information
    resultsDictionary["GX"] = gx;
    resultsDictionary["GY"] = gy;
    resultsDictionary["GZ"] = gz;

    // Step 6: return
    return resultsDictionary;
  }
}
