using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiModalParticipatingMassRatiosExtractor : IApplicationResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ResultsArrayProcessor _resultsArrayProcessor;

  public string ResultsKey => "modalParticipatingMassRatios";
  public ModelObjectType TargetObjectType => ModelObjectType.NONE;
  public ResultsConfiguration Configuration { get; } =
    new(
      ["LoadCase", "Wrap:StepNum"],
      ["Period", "UX", "UY", "UZ", "SumUX", "SumUY", "SumUZ", "RX", "RY", "RZ", "SumRX", "SumRY", "SumRZ"]
    );

  public CsiModalParticipatingMassRatiosExtractor(
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
      stepType = [];
    double[] stepNum = [],
      period = [],
      ux = [],
      uy = [],
      uz = [],
      sumUx = [],
      sumUy = [],
      sumUz = [],
      rx = [],
      ry = [],
      rz = [],
      sumRx = [],
      sumRy = [],
      sumRz = [];

    // Step 2: api call
    int success = _settingsStore.Current.SapModel.Results.ModalParticipatingMassRatios(
      ref numberResults,
      ref loadCase,
      ref stepType,
      ref stepNum,
      ref period,
      ref ux,
      ref uy,
      ref uz,
      ref sumUx,
      ref sumUy,
      ref sumUz,
      ref rx,
      ref ry,
      ref rz,
      ref sumRx,
      ref sumRy,
      ref sumRz
    );

    if (success != 0 || numberResults == 0)
    {
      throw new InvalidOperationException("Modal participating mass ratios extraction failed."); // shouldn't fail silently
    }

    // Step 3: organise array for dictionary processor
    var rawArrays = new Dictionary<string, object>
    {
      ["LoadCase"] = loadCase,
      ["StepNum"] = stepNum,
      ["Period"] = period,
      ["UX"] = ux,
      ["UY"] = uy,
      ["UZ"] = uz,
      ["SumUX"] = sumUx,
      ["SumUY"] = sumUy,
      ["SumUZ"] = sumUz,
      ["RX"] = rx,
      ["RY"] = ry,
      ["RZ"] = rz,
      ["SumRX"] = sumRx,
      ["SumRY"] = sumRy,
      ["SumRZ"] = sumRz
    };

    // Step 4: return sorted and processed dictionary
    var resultsDictionary = _resultsArrayProcessor.ProcessArrays(rawArrays, Configuration);

    // Step 5: return
    return resultsDictionary;
  }
}
