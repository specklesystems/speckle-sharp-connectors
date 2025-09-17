using System.ComponentModel;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class DetailLevelSetting(DetailLevelType value) : ICardSetting
{
  public string? Id { get; set; } = "detailLevel";
  public string? Title { get; set; } = "Detail Level";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(DetailLevelType)).ToList();

  [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
  [DefaultValue("Medium")]
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, DetailLevelType> GeometryFidelityMap = System
    .Enum.GetValues(typeof(DetailLevelType))
    .Cast<DetailLevelType>()
    .ToDictionary(v => v.ToString(), v => v);
}
