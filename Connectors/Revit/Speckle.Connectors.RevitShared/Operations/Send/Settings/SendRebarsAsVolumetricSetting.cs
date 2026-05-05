using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendRebarsAsVolumetricSetting(bool value = SendRebarsAsVolumetricSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "sendRebarsAsVolumetric";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Send Rebars As Volumetric";
  public string? Description { get; set; } = "Sends rebars as solid 3D geometry instead of centre-line curves. Disable for faster sends on rebar-heavy models.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
