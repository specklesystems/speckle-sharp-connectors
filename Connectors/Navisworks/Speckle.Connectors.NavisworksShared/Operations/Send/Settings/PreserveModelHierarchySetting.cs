using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class PreserveModelHierarchySetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "preserveModelHierarchy";
  public string? Title { get; set; } = "Preserve Model Hierarchy";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
