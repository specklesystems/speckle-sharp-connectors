using System.ComponentModel;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connectors.Revit.Operations.Receive.Settings;

public class ReferencePointSetting(ReceiveReferencePointType value) : ICardSetting
{
  public string? Id { get; set; } = "referencePoint";
  public string? Title { get; set; } = "Reference Point";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(ReceiveReferencePointType)).ToList();

  [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
  [DefaultValue("Source")]
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, ReceiveReferencePointType> ReferencePointMap = System
    .Enum.GetValues(typeof(ReceiveReferencePointType))
    .Cast<ReceiveReferencePointType>()
    .ToDictionary(v => v.ToString(), v => v);
}
