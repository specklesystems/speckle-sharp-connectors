using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendRebarsAsVolumetricSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "sendRebarsAsVolumetric";
  public string? Title { get; set; } = "Send Rebars As Volumetric (disable for better performance)";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
