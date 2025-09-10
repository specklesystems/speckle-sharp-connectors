using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiModalParticipationFactorsExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;
  public string ResultsKey => "modalParticipationFactors";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;
  public ResultsConfiguration Configuration { get; } =
    new(["LoadCase", "Wrap:Mode"], ["Period", "UX", "UY", "UZ", "RX", "RY", "RZ", "ModalMass", "ModalStiff"]);

  public CsiModalParticipationFactorsExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ResultsArrayProcessor resultsArrayProcessor
  )
  {
    _settingsStore = settingsStore;
    _resultsArrayProcessor = resultsArrayProcessor;
  }

  // NOTE: global reactions, they're independent of the user selection, therefore discarded and TargetObjectType is none
  public Dictionary<string, object> GetResults(IEnumerable<string>? _)
  {
    // Step 1: define api variables
    int numberResults = 0;
    string[] loadCase = [],
      mode = [];
    double[] modeNum = [],
      period = [],
      ux = [],
      uy = [],
      uz = [],
      rx = [],
      ry = [],
      rz = [],
      modalMass = [],
      modalStiff = [];

    // Step 2: api call
    int success = _settingsStore.Current.SapModel.Results.ModalParticipationFactors(
      ref numberResults,
      ref loadCase,
      ref mode,
      ref modeNum,
      ref period,
      ref ux,
      ref uy,
      ref uz,
      ref rx,
      ref ry,
      ref rz,
      ref modalMass,
      ref modalStiff
    );

    if (success != 0 || numberResults == 0)
    {
      throw new InvalidOperationException("Modal participation factors extraction failed."); // shouldn't fail silently
    }

    // Step 3: organise array for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["LoadCase"] = loadCase,
      ["Mode"] = modeNum,
      ["Period"] = period,
      ["UX"] = ux,
      ["UY"] = uy,
      ["UZ"] = uz,
      ["RX"] = rx,
      ["RY"] = ry,
      ["RZ"] = rz,
      ["ModalMass"] = modalMass,
      ["ModalStiff"] = modalStiff,
    };

    // Step 4: return sorted and processed dictionary
    var resultsDictionary = _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);

    // Step 5: return
    return resultsDictionary;
  }
}
