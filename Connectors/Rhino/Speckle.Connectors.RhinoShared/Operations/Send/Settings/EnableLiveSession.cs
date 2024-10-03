using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class EnableLiveSession(bool value) : ICardSetting
{
  public string? Id { get; set; } = "enableLiveSession";
  public string? Title { get; set; } = "Enable Live Session";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
