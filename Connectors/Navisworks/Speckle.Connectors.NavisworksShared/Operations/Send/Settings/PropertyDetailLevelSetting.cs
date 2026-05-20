using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class PropertyDetailLevelSetting(PropertyDetailLevel value) : ICardSetting
{
  public string? Id { get; set; } = "propertyDetailLevel";
  public string? Title { get; set; } = "Property Detail";
  public string? Description { get; set; } = "All, None, or Standard property payloads.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } =
  [nameof(PropertyDetailLevel.All), nameof(PropertyDetailLevel.None), nameof(PropertyDetailLevel.Standard)];
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, PropertyDetailLevel> PropertyDetailLevelMap = BuildPropertyDetailLevelMap();

  private static Dictionary<string, PropertyDetailLevel> BuildPropertyDetailLevelMap()
  {
    var result = System
      .Enum.GetValues(typeof(PropertyDetailLevel))
      .Cast<PropertyDetailLevel>()
      .ToDictionary(v => v.ToString(), v => v);
    // Backward compatibility for saved cards
    result["Essential"] = PropertyDetailLevel.Standard;
    result["Full"] = PropertyDetailLevel.All;
    return result;
  }
}
