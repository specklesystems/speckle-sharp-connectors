using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendAllDesignOptionsSetting(bool value = SendAllDesignOptionsSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "sendAllDesignOptions";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Publish All Design Options";
  public string? Description { get; set; } =
    "When publishing a view, also publishes every design option the view doesn't show. Category publishes already include every option.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
