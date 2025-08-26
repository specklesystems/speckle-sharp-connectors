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
      return ValidateLoadCasesAreComplete(selectedLoadCases);
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

  private AnalysisResultsValidation ValidateLoadCasesAreComplete(List<string> selectedLoadCases)
  {
    // get status for all cases
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

    // build lookup dictionary used to check the specific cases / combinations from user selection in ui
    var statusLookup = caseNames.Zip(statuses).ToDictionary(x => x.First, x => x.Second);

    // check that each selected case is finished
    var notFinished = new List<string>();
    foreach (var selectedCase in selectedLoadCases)
    {
      if (statusLookup.TryGetValue(selectedCase, out int status))
      {
        if (status != 4) // 1 = Not run, 2 = Could not start, 3 = Not finished, 4 = Finished
        {
          notFinished.Add($"{selectedCase}");
        }
      }
      else // NOTE: this should never happen
      {
        throw new InvalidOperationException(
          $"{selectedCase} is not present in the model. Indicates stale cases and / or combinations."
        );
      }
    }

    if (notFinished.Count != 0)
    {
      string errorMessage =
        $"Analysis not complete for requested cases/combinations: {string.Join(", ", notFinished)}. Run analysis first.";
      return new AnalysisResultsValidation(false, errorMessage);
    }

    return new AnalysisResultsValidation(true);
  }
}
