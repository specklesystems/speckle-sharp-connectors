using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class DetailLevelSetting(DetailLevelType value = DetailLevelSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "detailLevel";
  public const DetailLevelType DEFAULT_VALUE = DetailLevelType.Medium;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Detail Level";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(DetailLevelType)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, DetailLevelType> GeometryFidelityMap = System
    .Enum.GetValues(typeof(DetailLevelType))
    .Cast<DetailLevelType>()
    .ToDictionary(v => v.ToString(), v => v);
}
