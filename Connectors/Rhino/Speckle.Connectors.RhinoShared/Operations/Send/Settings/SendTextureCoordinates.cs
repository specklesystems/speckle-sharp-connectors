using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class SendTextureCoordinates(bool value) : ICardSetting
{
  public string? Id { get; set; } = "sendTextureCoordinates";
  public string? Title { get; set; } = "Send Texture Coordinates";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
