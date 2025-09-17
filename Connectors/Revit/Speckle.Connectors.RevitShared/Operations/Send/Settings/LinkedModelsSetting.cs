using System.ComponentModel;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.RevitShared.Operations;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class LinkedModelsSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = RevitSettingsConstants.INCLUDE_LINKED_MODELS;
  public string? Title { get; set; } = "Include Linked Models";
  public string? Type { get; set; } = "boolean";

  [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
  [DefaultValue(true)]
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
