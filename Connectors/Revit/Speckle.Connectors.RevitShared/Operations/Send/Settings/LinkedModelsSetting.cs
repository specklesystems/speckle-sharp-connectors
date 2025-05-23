using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class LinkedModelsSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "includeLinkedModels";
  public string? Title { get; set; } = "Include Linked Models";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
