using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class IncludeInternalPropertiesSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "includeInternalProperties";
  public string? Title { get; set; } = "Include Internal Properties";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
