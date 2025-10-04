using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendMaterialCustomParameters(bool value = SendMaterialCustomParameters.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "sendMaterialCustomParameters";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Send Material Custom Parameters (disable for better performance)";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
