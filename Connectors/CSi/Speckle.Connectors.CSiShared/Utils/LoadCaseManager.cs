using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;

namespace Speckle.Connectors.CSiShared.Utils;

public class LoadCaseManager
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettingsStore;

  public LoadCaseManager(IConverterSettingsStore<CsiConversionSettings> converterSettingsStore)
  {
    _converterSettingsStore = converterSettingsStore;
  }

  public void ConfigureSelectedLoadCases(List<string> selectedLoadCases)
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
}
