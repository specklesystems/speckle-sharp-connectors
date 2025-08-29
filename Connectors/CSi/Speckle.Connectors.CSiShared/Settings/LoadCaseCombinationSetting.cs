using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.CSiShared.Settings;

public class LoadCaseCombinationSetting(List<string> values, cSapModel sapModel) : ICardSetting
{
  public string? Id { get; set; } = "loadCasesAndCombinations";
  public string? Title { get; set; } = "Load Cases & Combinations";
  public string? Type { get; set; } = "array";
  public object? Value { get; set; } = values;
  public List<string>? Enum { get; set; } = LoadCaseHelper.GetLoadCasesAndCombinations(sapModel);
}
