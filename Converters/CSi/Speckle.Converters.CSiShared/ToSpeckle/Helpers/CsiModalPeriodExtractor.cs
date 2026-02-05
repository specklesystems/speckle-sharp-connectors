using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiModalPeriodExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;
  public string ResultsKey => "modalPeriodsAndFrequencies";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;
  public ResultsConfiguration Configuration { get; } =
    new(["LoadCase", "Wrap:Mode"], ["Period", "Frequency", "CircFreq", "Eigenvalue"]);

  public CsiModalPeriodExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  public Dictionary<string, object> GetResults(IEnumerable<string>? _)
  {
    // Step 1: define api variables
    int numberResults = 0;
    string[] loadCase = [],
      mode = [];
    double[] modeNum = [],
      period = [],
      frequency = [],
      circFreq = [],
      eigenValue = [];

    // Step 2: api call
    int success = _settingsStore.Current.SapModel.Results.ModalPeriod(
      ref numberResults,
      ref loadCase,
      ref mode,
      ref modeNum,
      ref period,
      ref frequency,
      ref circFreq,
      ref eigenValue
    );

    if (success != 0 || numberResults == 0)
    {
      throw new InvalidOperationException("Modal participating mass ratios extraction failed."); // shouldn't fail silently
    }

    // Step 3: organise array for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["LoadCase"] = loadCase,
      ["Mode"] = modeNum,
      ["Period"] = period,
      ["Frequency"] = frequency,
      ["CircFreq"] = circFreq,
      ["Eigenvalue"] = eigenValue,
    };

    // Step 4: return sorted and processed dictionary
    var resultsDictionary = _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);

    // Step 5: return
    return resultsDictionary;
  }
}
