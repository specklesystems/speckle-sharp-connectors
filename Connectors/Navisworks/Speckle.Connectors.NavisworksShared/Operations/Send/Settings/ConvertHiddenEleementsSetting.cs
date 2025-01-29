using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class ConvertHiddenElementsSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "convertHiddenElements";
  public string? Title { get; set; } = "Convert Hidden Elements";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
