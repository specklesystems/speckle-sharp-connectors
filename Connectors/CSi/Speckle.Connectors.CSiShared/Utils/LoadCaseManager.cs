using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk;

namespace Speckle.Connectors.CSiShared.Utils;

public record AnalysisResultsValidation(bool IsValid, string? ErrorMessage = null);

public class LoadCaseManager
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettingsStore;

  public LoadCaseManager(IConverterSettingsStore<CsiConversionSettings> converterSettingsStore)
  {
    _converterSettingsStore = converterSettingsStore;
  }

  /// <summary>
  /// Responsible for two things. Firstly, we need to setup the results so that only the requested cases and combinations
  /// are published. Secondly, we need to ensure that the requested cases and combinations are actually run.
  /// </summary>
  public AnalysisResultsValidation ConfigureAndValidateSelectedLoadCases(List<string> selectedLoadCases)
  {
    try
    {
      // step 1: configure load cases for output
      ConfigureSelectedLoadCases(selectedLoadCases);

      // step 2: validate they are complete
      return ValidateSelectedCasesAreComplete(selectedLoadCases);
    }
    catch (Exception ex) when (ex.IsFatal())
    {
      return new AnalysisResultsValidation(false, ex.Message);
    }
  }

  private void ConfigureSelectedLoadCases(List<string> selectedLoadCases)
  {
    // deselect all load cases and combos
    _converterSettingsStore.Current.SapModel.Results.Setup.DeselectAllCasesAndCombosForOutput();

    // ui presents cases and combinations as a flat list. we need to distinguish if the string is a case or combo
    // do this by seeing if the string is within the list of defined cases
    // typically defined load cases << defined load combinations, so this approach should be more efficient
    int numberOfLoadCases = 0;
    string[] loadCaseNames = [];
    _converterSettingsStore.Current.SapModel.LoadCases.GetNameList(ref numberOfLoadCases, ref loadCaseNames);

    // set user selected combos to true (i.e. to export)
    foreach (var selectedLoadCase in selectedLoadCases)
    {
      int success = loadCaseNames.Contains(selectedLoadCase)
        ? _converterSettingsStore.Current.SapModel.Results.Setup.SetCaseSelectedForOutput(selectedLoadCase)
        : _converterSettingsStore.Current.SapModel.Results.Setup.SetComboSelectedForOutput(selectedLoadCase);

      // ui should only present valid options
      // `LoadCaseManager` only fetches load cases and load combinations (not patterns), so this should never throw
      if (success != 0)
      {
        throw new InvalidOperationException($"Failed to set {selectedLoadCase} for output.");
      }
    }
  }

  private AnalysisResultsValidation ValidateSelectedCasesAreComplete(List<string> selectedCasesAndCombinations)
  {
    // get status for all load cases (combinations not included in this API call)
    int numberItems = 0;
    string[] caseNames = [];
    int[] statuses = [];

    int result = _converterSettingsStore.Current.SapModel.Analyze.GetCaseStatus(
      ref numberItems,
      ref caseNames,
      ref statuses
    );

    if (result != 0)
    {
      return new AnalysisResultsValidation(false, "Failed to retrieve load case status from model.");
    }

    // build lookup dictionary for load cases only
    var caseStatusLookup = caseNames.Zip(statuses).ToDictionary(x => x.First, x => x.Second);

    // separate selected items into cases and combinations
    var selectedCases = selectedCasesAndCombinations.Where(item => caseStatusLookup.ContainsKey(item)).ToList();
    var selectedCombinations = selectedCasesAndCombinations.Except(selectedCases).ToList();

    // validate load cases status
    var notFinishedCases = new List<string>();
    foreach (var caseName in selectedCases)
    {
      int status = caseStatusLookup[caseName];
      if (status != 4) // 1 = Not run, 2 = Could not start, 3 = Not finished, 4 = Finished
      {
        notFinishedCases.Add($"{caseName}");
      }
    }

    // TODO: Validate load combinations status
    // for now, assume combinations are valid if we can't validate them
    if (selectedCombinations.Count != 0)
    {
      // combinations validation not implemented - assuming they're valid for now
      // it'll get complicated, we can't get the status of a combination, so we need to check the constituent cases
    }

    if (notFinishedCases.Count != 0)
    {
      string errorMessage =
        $"Analysis not complete for load cases: {string.Join(", ", notFinishedCases)}. Run analysis first.";
      return new AnalysisResultsValidation(false, errorMessage);
    }

    return new AnalysisResultsValidation(true);
  }
}
