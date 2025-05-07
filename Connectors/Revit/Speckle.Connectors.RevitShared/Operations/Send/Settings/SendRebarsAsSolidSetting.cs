using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendRebarsAsSolidSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "sendRebarsAsSolid";
  public string? Title { get; set; } = "Send Rebars As Solid";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
