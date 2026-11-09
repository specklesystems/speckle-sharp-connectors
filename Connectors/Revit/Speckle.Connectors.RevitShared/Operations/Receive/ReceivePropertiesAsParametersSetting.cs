using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class ReceivePropertiesAsParametersSetting(bool value = ReceivePropertiesAsParametersSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "receivePropertiesAsParametersSetting";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Receive Properties as Parameters";
  public string? Description { get; set; } =
    "Write each object's properties (Rhino user text, parameters from other models) onto the created element as instance parameters. Adds shared parameters to the project.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
