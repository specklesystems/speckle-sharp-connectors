using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.RevitShared.Operations;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class LinkedModelsSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = RevitSettingsConstants.INCLUDE_LINKED_MODELS;
  public string? Title { get; set; } = "Include Linked Models";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
