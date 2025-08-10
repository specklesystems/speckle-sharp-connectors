using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.CSiShared.Settings;

public class ResultTypeSetting(List<string> values) : ICardSetting
{
  public string? Id { get; set; } = "resultTypes";
  public string? Title { get; set; } = "Result Type";
  public string? Type { get; set; } = "array";
  public object? Value { get; set; } = values;
  public List<string>? Enum { get; set; } = ["FrameForces"];
}
