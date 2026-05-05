using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.TeklaShared.Operations.Send.Settings;

public class SendRebarsAsSolidSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "sendRebarsAsSolid";
  public string? Title { get; set; } = "Send Rebars As Solid";
  public string? Description { get; set; } = "Sends rebar as solid 3D geometry instead of centre-line curves. Disable for faster sends on rebar-heavy models.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
