using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.CSiShared.Settings;

public class LoadCaseCombinationSetting(List<string> values) : ICardSetting
{
  public string? Id { get; set; } = "loadCasesAndCombinations";
  public string? Title { get; set; } = "Load Cases & Combinations";
  public string? Type { get; set; } = "array";
  public object? Value { get; set; } = values;
  public List<string>? Enum { get; set; } = new List<string> { "Dead Load", "Live Load", "1.4DL", "1.2DL + 1.6LL", };
}
