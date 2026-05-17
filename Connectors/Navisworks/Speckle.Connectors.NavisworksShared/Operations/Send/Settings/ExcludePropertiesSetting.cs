using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class ExcludePropertiesSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "excludeProperties";
  public string? Title { get; set; } = "Exclude Properties";

  public string? Description { get; set; }

  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
