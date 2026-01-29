using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendAreasAsMeshSetting(bool value = SendAreasAsMeshSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "sendAreasAsMesh";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Send Areas As Mesh";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
