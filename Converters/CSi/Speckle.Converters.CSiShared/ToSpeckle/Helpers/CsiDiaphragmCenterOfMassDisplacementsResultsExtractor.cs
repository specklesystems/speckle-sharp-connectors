using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiDiaphragmCenterOfMassDisplacementsResultsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly DatabaseTableExtractor _databaseTableExtractor;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

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

  public Dictionary<string, object> GetResults(IEnumerable<string>? objectNames = null) =>
    throw new NotImplementedException();
}
