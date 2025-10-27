using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiStoryForceResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly DatabaseTableExtractor _databaseTableExtractor;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  private const string AXIAL_FORCE = "P";
  private const string LOAD_CASE = "LoadCase";
  private const string LOCATION = "Location";
  private const string MAJOR_MOMENT = "MX";
  private const string MAJOR_SHEAR = "VX";
  private const string MINOR_MOMENT = "MY";
  private const string MINOR_SHEAR = "VY";
  private const string OUTPUT_CASE = "OutputCase";
  private const string STORY = "Story";
  private const string STORY_FORCES = "Story Forces";
  private const string TORSION = "T";

  public string ResultsKey => "storyForces";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;
  public ResultsConfiguration Configuration { get; } =
    new([STORY, LOAD_CASE, LOCATION], [AXIAL_FORCE, MAJOR_SHEAR, MINOR_SHEAR, TORSION, MAJOR_MOMENT, MINOR_MOMENT]);

  public CsiStoryForceResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DatabaseTableExtractor databaseTableExtractor,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _databaseTableExtractor = databaseTableExtractor;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  // NOTE: these aren't object specific, they're independent of the user selection, therefore discared
  public Dictionary<string, object> GetResults(IEnumerable<string>? objectNames = null)
  {
    // Step 1: use DatabaseTableExtractor to get results
    // NOTE: this differs from other results since Story Forces doesn't have a SapModel.Results.StoryForces method
    var tableData = _databaseTableExtractor
      .GetTableData(STORY_FORCES, STORY, additionalKeyColumns: [OUTPUT_CASE, LOCATION])
      .Rows;

    // Get user selected load cases and combinations for filtering
    var userSelectedLoadCases = _settingsStore.Current.SelectedLoadCasesAndCombinations?.ToHashSet();

    if (userSelectedLoadCases == null)
    {
      // NOTE: this should never happen as we validate in root object builder
      throw new InvalidOperationException("No load cases or combinations selected");
    }

    // Step 2: Filter out entries that don't match user's selected load cases/combinations
    // and organize arrays for dictionary processor
    var filteredEntries = tableData
      .Where(entry =>
        userSelectedLoadCases.Count == 0
        || userSelectedLoadCases.Contains(ResultsExtractorHelper.GetOutputCase(entry.Value, OUTPUT_CASE))
      )
      .ToList();

    if (filteredEntries.Count == 0)
    {
      throw new InvalidOperationException(
        "No load cases or combinations in database match user-selected load cases and combinations"
      ); // shouldn't fail silently
    }

    // Step 3: Extract arrays from the nested dictionary structure
    var stories = new string[filteredEntries.Count];
    var loadCases = new string[filteredEntries.Count];
    var locations = new string[filteredEntries.Count];
    var pValues = new double[filteredEntries.Count];
    var vxValues = new double[filteredEntries.Count];
    var vyValues = new double[filteredEntries.Count];
    var tValues = new double[filteredEntries.Count];
    var mxValues = new double[filteredEntries.Count];
    var myValues = new double[filteredEntries.Count];

    for (int i = 0; i < filteredEntries.Count; i++)
    {
      var entry = filteredEntries[i];
      var nestedDict = entry.Value;

      // Extract Story, OutputCase, and Location directly from the nested dictionary
      if (!nestedDict.TryGetValue(STORY, out var story) || string.IsNullOrEmpty(story))
      {
        throw new InvalidOperationException($"Missing or empty 'Story' column in database row {i}");
      }
      stories[i] = story;
      loadCases[i] = nestedDict.TryGetValue(OUTPUT_CASE, out var loadCase) ? loadCase : string.Empty;
      locations[i] = nestedDict.TryGetValue(LOCATION, out var location) ? location : string.Empty;

      // Extract force values directly from nested dictionary using field names as keys
      pValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(AXIAL_FORCE, out var p) ? p : null);
      vxValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(MAJOR_SHEAR, out var vx) ? vx : null);
      vyValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(MINOR_SHEAR, out var vy) ? vy : null);
      tValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(TORSION, out var t) ? t : null);
      mxValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(MAJOR_MOMENT, out var mx) ? mx : null);
      myValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(MINOR_MOMENT, out var my) ? my : null);
    }

    var rawArrays = new Dictionary<string, object>
    {
      [STORY] = stories,
      [LOAD_CASE] = loadCases,
      [LOCATION] = locations,
      [AXIAL_FORCE] = pValues,
      [MAJOR_SHEAR] = vxValues,
      [MINOR_SHEAR] = vyValues,
      [TORSION] = tValues,
      [MAJOR_MOMENT] = mxValues,
      [MINOR_MOMENT] = myValues
    };

    // Step 4: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
