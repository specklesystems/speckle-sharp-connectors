using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.RevitShared.Operations;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendParameterNullOrEmptyStringsSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = RevitSettingsConstants.SEND_NULL_EMPTY_PARAMS;
  public string? Title { get; set; } = "Send null/empty parameters";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
