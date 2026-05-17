using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class PropertyDetailLevelSetting(PropertyDetailLevel value) : ICardSetting
{
  public string? Id { get; set; } = "propertyDetailLevel";
  public string? Title { get; set; } = "Property Detail";
  public string? Description { get; set; } = "Controls how much property data is extracted and sent.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(PropertyDetailLevel)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, PropertyDetailLevel> PropertyDetailLevelMap = System
    .Enum.GetValues(typeof(PropertyDetailLevel))
    .Cast<PropertyDetailLevel>()
    .ToDictionary(v => v.ToString(), v => v);
}
