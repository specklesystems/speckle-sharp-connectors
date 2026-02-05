using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiStoryDriftsResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;
  public string ResultsKey => "storyDrifts";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;

  public ResultsConfiguration Configuration { get; } =
    new(["Story", "LoadCase", "Wrap:StepNum"], ["Direction", "Drift", "Label", "X", "Y", "Z"]);

  public CsiStoryDriftsResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  // NOTE: these aren't object specific, they're independent of the user selection, therefore discarded
  public Dictionary<string, object> GetResults(IEnumerable<string>? objectNames = null)
  {
    // Step 1: define api variables
    int numberResults = 0;
    string[] story = [],
      loadCase = [],
      stepType = [],
      direction = [],
      label = [];
    double[] stepNum = [],
      drift = [],
      x = [],
      y = [],
      z = [];

    // Step 2: api call
    int success = _settingsStore.Current.SapModel.Results.StoryDrifts(
      ref numberResults,
      ref story,
      ref loadCase,
      ref stepType,
      ref stepNum,
      ref direction,
      ref drift,
      ref label,
      ref x,
      ref y,
      ref z
    );

    if (success != 0 || numberResults == 0)
    {
      throw new InvalidOperationException("Story drifts extraction failed."); // shouldn't fail silently
    }

    // Step 3: organise arrays for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["Story"] = story,
      ["LoadCase"] = loadCase,
      ["StepNum"] = stepNum,
      ["Direction"] = direction,
      ["Drift"] = drift,
      ["Label"] = label,
      ["X"] = x,
      ["Y"] = y,
      ["Z"] = z
    };

    // Step 4: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
