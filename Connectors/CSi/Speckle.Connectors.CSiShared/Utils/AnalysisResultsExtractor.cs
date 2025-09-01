using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.CSiShared.Utils;

public class AnalysisResultsExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettingsStore;
  private readonly CsiResultsExtractorFactory _resultsExtractorFactory;

  public AnalysisResultsExtractor(
    IConverterSettingsStore<CsiConversionSettings> converterSettingsStore,
    CsiResultsExtractorFactory resultsExtractorFactory
  )
  {
    _converterSettingsStore = converterSettingsStore;
    _resultsExtractorFactory = resultsExtractorFactory;
  }

  /// <summary>
  /// Extracts complete analysis results including units retrieval, load case configuration, and results extraction.
  /// Assumes inputs have been validated by caller.
  /// </summary>
  public Base ExtractAnalysisResults(
    List<string> selectedCasesAndCombinations,
    List<string> requestedResultTypes,
    Dictionary<ModelObjectType, List<string>> objectSelectionSummary
  )
  {
    // Step 1: get analysis units
    var analysisResults = CreateAnalysisResultsWithUnits();

    // Step 2: configure and validate load cases
    ConfigureAndValidateSelectedLoadCases(selectedCasesAndCombinations);

    // Step 3: extract results using clean factory pattern
    ExtractResults(requestedResultTypes, objectSelectionSummary, analysisResults);

    return analysisResults;
  }

  /// <summary>
  /// Instantiates a Base object and pre-populates it with the models defined force units.
  /// </summary>
  /// <returns></returns>
  /// <exception cref="SpeckleException"></exception>
  private Base CreateAnalysisResultsWithUnits()
  {
    var forceUnit = eForce.NotApplicable;
    var lengthUnit = eLength.NotApplicable;
    var temperatureUnit = eTemperature.NotApplicable;

    int success = _converterSettingsStore.Current.SapModel.GetDatabaseUnits_2(
      ref forceUnit,
      ref lengthUnit,
      ref temperatureUnit
    );

    if (success != 0)
    {
      throw new SpeckleException("Failed to retrieve units for analysis results");
    }

    return new Base
    {
      ["forceUnit"] = forceUnit.ToString(),
      ["lengthUnit"] = lengthUnit.ToString(),
      ["temperatureUnit"] = temperatureUnit.ToString()
    };
  }

  private void ExtractResults(
    List<string> requestedResultTypes,
    Dictionary<ModelObjectType, List<string>> objectSelectionSummary,
    Base analysisResults
  )
  {
    foreach (var resultType in requestedResultTypes)
    {
      var extractor = _resultsExtractorFactory.GetExtractor(resultType);
      objectSelectionSummary.TryGetValue(extractor.TargetObjectType, out var objectNames);
      analysisResults[extractor.ResultsKey] = extractor.GetResults(objectNames);
    }
  }

  /// <summary>
  /// Responsible for two things. Firstly, we need to setup the results so that only the requested cases and combinations
  /// are published. Secondly, we need to ensure that the requested cases and combinations are actually run.
  /// </summary>
  private void ConfigureAndValidateSelectedLoadCases(List<string> selectedLoadCases)
  {
    // step 1: configure load cases for output
    ConfigureSelectedLoadCases(selectedLoadCases);

    // step 2: validate they are complete (throws on failure)
    ValidateSelectedCasesAreComplete(selectedLoadCases);
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
      // `AnalysisResultsExtractor` only fetches load cases and load combinations (not patterns), so this should never throw
      if (success != 0)
      {
        throw new InvalidOperationException($"Failed to set {selectedLoadCase} for output.");
      }
    }
  }

  private void ValidateSelectedCasesAreComplete(List<string> selectedCasesAndCombinations)
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
      throw new SpeckleException("Failed to retrieve load case status from model.");
    }

    // build lookup dictionary for load cases only
    var caseStatusLookup = caseNames
      .Zip(statuses, (name, status) => new { name, status })
      .ToDictionary(x => x.name, x => x.status);

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
      throw new SpeckleException(errorMessage);
    }
  }
}
