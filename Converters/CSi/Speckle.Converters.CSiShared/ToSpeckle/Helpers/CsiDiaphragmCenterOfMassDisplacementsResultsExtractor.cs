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
  }
}
