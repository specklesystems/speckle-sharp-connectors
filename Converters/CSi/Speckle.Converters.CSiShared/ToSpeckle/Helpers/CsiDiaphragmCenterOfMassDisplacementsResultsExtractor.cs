using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiDiaphragmCenterOfMassDisplacementsResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly DatabaseTableExtractor _databaseTableExtractor;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  private const string TABLE_KEY = "Diaphragm Center Of Mass Displacements";
  private const string STORY = "Story";
  private const string DIAPHRAGM = "Diaphragm";
  private const string LOAD_CASE = "LoadCase";
  private const string STEP_NUM = "StepNum";
  private const string UX = "UX";
  private const string UY = "UY";
  private const string RZ = "RZ";
  private const string POINT = "Point";
  private const string X = "X";
  private const string Y = "Y";
  private const string Z = "Z";

  public string ResultsKey => "diaphragmCenterOfMassDisplacements";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;
  public ResultsConfiguration Configuration { get; } =
    new([STORY, DIAPHRAGM, LOAD_CASE, STEP_NUM], [UX, UY, RZ, POINT, X, Y, Z]);

  public CsiDiaphragmCenterOfMassDisplacementsResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DatabaseTableExtractor tableExtractor,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _databaseTableExtractor = tableExtractor;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  public Dictionary<string, object> GetResults(IEnumerable<string>? objectNames = null)
  {
    // Step 1: use DatabaseTableExtractor to get results
    // NOTE: this differs from other results since diaphragm center of mass displacements don't have a
    // SapModel.Results method
    var tableData = _databaseTableExtractor
      .GetTableData(TABLE_KEY, STORY, additionalKeyColumns: [LOAD_CASE, DIAPHRAGM])
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
        || userSelectedLoadCases.Contains(ResultsExtractorHelper.GetOutputCase(entry.Value, LOAD_CASE))
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
    var diaphragms = new string[filteredEntries.Count];
    var loadCases = new string[filteredEntries.Count];
    var stepNums = new string[filteredEntries.Count];
    var uxValues = new double[filteredEntries.Count];
    var uyValues = new double[filteredEntries.Count];
    var rzValues = new double[filteredEntries.Count];
    var pointValues = new string[filteredEntries.Count];
    var xValues = new double[filteredEntries.Count];
    var yValues = new double[filteredEntries.Count];
    var zValues = new double[filteredEntries.Count];

    for (int i = 0; i < filteredEntries.Count; i++)
    {
      var entry = filteredEntries[i];
      var nestedDict = entry.Value;

      // Extract Story, Diaphragm, LoadCase, StepNum, PointValues and Location directly from the nested dictionary
      if (!nestedDict.TryGetValue(STORY, out var story) || string.IsNullOrEmpty(story))
      {
        throw new InvalidOperationException($"Missing or empty 'Story' column in database row {i}");
      }
      stories[i] = story;
      diaphragms[i] = nestedDict.TryGetValue(DIAPHRAGM, out var diaphragm) ? diaphragm : string.Empty;
      loadCases[i] = nestedDict.TryGetValue(LOAD_CASE, out var loadCase) ? loadCase : string.Empty;
      stepNums[i] = nestedDict.TryGetValue(STEP_NUM, out var step) ? step : string.Empty;
      pointValues[i] = nestedDict.TryGetValue(POINT, out var point) ? point : string.Empty;

      // Extract numeric values directly from nested dictionary using field names as keys
      uxValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(UX, out var ux) ? ux : null);
      uyValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(UY, out var uy) ? uy : null);
      rzValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(RZ, out var rz) ? rz : null);
      xValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(X, out var x) ? x : null);
      yValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(Y, out var y) ? y : null);
      zValues[i] = ResultsExtractorHelper.TryParseDouble(nestedDict.TryGetValue(Z, out var z) ? z : null);
    }

    var rawArrays = new Dictionary<string, object>
    {
      [STORY] = stories,
      [DIAPHRAGM] = diaphragms,
      [LOAD_CASE] = loadCases,
      [STEP_NUM] = stepNums,
      [POINT] = pointValues,
      [UX] = uxValues,
      [UY] = uyValues,
      [RZ] = rzValues,
      [X] = xValues,
      [Y] = yValues,
      [Z] = zValues
    };

    // Step 4: return sorted and processed dictionary
    return _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);
  }
}
