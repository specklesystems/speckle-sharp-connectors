using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.TSDShared.Settings;

internal sealed class TsdLoadingSetting(List<string> values, IReadOnlyList<string> loadingNames) : ICardSetting
{
  public string? Id { get; set; } = "loadCasesAndCombinations";
  public string? Title { get; set; } = "Load Cases & Combinations";
  public string? Description { get; set; }
  public string? Type { get; set; } = "array";
  public object? Value { get; set; } = values;
  public List<string>? Enum { get; set; } = loadingNames.OrderBy(name => name).ToList();
}
