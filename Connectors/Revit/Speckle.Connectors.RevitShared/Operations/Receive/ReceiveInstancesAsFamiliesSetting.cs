using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class ReceiveInstancesAsFamiliesSetting(bool value = ReceiveInstancesAsFamiliesSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "receiveInstancesAsFamiliesSetting";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Receive Blocks as Families";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
