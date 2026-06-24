using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.TSDShared.Results;

namespace Speckle.Connectors.TSDShared.Settings;

internal sealed class TsdResultTypeSetting(List<string> values) : ICardSetting
{
  public string? Id { get; set; } = "resultTypes";
  public string? Title { get; set; } = "Result Type";
  public string? Description { get; set; }
  public string? Type { get; set; } = "array";
  public object? Value { get; set; } = values;
  public List<string>? Enum { get; set; } = TsdResultsKeys.All.OrderBy(key => key).ToList();
}
