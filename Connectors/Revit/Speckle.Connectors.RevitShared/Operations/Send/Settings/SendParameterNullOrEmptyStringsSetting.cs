using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendParameterNullOrEmptyStringsSetting(bool value = SendParameterNullOrEmptyStringsSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "nullemptyparams";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Send null/empty parameters";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
