using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class PropertyDetailLevelSetting(PropertyDetailLevel value) : ICardSetting
{
  public string? Id { get; set; } = "propertyDetailLevel";
  public string? Title { get; set; } = "Property Detail";
  public string? Description { get; set; } =
    "Controls how much property data is extracted and sent. Choose None to send geometry only.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } =
  [
    nameof(PropertyDetailLevel.None),
    nameof(PropertyDetailLevel.Essential),
    nameof(PropertyDetailLevel.Standard),
    nameof(PropertyDetailLevel.Full),
  ];
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, PropertyDetailLevel> PropertyDetailLevelMap = System
    .Enum.GetValues(typeof(PropertyDetailLevel))
    .Cast<PropertyDetailLevel>()
    .ToDictionary(v => v.ToString(), v => v);
}
