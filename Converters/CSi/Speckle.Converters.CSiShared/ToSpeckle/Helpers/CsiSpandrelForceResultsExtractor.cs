using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiSpandrelForceResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;
  public string ResultsKey => "spandrelForces";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;

  public ResultsConfiguration Configuration { get; } =
    new(["SpandrelName", "StoryName", "LoadCase", "Wrap:Location"], ["P", "V2", "V3", "T", "M2", "M3"]);

  public CsiSpandrelForceResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  // NOTE: since these spandrel assignments aren't object specific, they're independent of the user selection, therefore discarded
  public Dictionary<string, object> GetResults(IEnumerable<string>? _)
  {
    // Step 1: define api variables
    int numberResults = 0;
    string[] storyName = [],
      spandrelName = [],
      loadCase = [],
      location = [];
    double[] p = [],
      v2 = [],
      v3 = [],
      t = [],
      m2 = [],
      m3 = [];

    // Step 2: api call
    int success = _settingsStore.Current.SapModel.Results.SpandrelForce(
      ref numberResults,
      ref storyName,
      ref spandrelName,
      ref loadCase,
      ref location,
      ref p,
      ref v2,
      ref v3,
      ref t,
      ref m2,
      ref m3
    );

    if (success != 0 || numberResults == 0)
    {
      throw new InvalidOperationException("Spandrel force extraction failed."); // shouldn't fail silently
    }

    // Step 3: organise arrays for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["StoryName"] = storyName,
      ["SpandrelName"] = spandrelName,
      ["LoadCase"] = loadCase,
      ["Location"] = location,
      ["P"] = p,
      ["V2"] = v2,
      ["V3"] = v3,
      ["T"] = t,
      ["M2"] = m2,
      ["M3"] = m3,
    };

    // Step 4: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
