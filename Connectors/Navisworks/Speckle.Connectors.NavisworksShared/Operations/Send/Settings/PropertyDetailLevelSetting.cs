using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class PropertyDetailLevelSetting(PropertyDetailLevel value = PropertyDetailLevelSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "propertyDetailLevel";
  public const PropertyDetailLevel DEFAULT_VALUE = PropertyDetailLevel.Standard;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Property Detail";
  public string? Description { get; set; } =
    "All: user-filtered properties. Standard: filtered minus exclusions unless in Quick Properties. None: Item only.";
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
