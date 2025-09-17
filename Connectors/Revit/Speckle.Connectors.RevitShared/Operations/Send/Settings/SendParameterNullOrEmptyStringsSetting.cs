using System.ComponentModel;
using Speckle.Connectors.DUI.Settings;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendParameterNullOrEmptyStringsSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "nullemptyparams";
  public string? Title { get; set; } = "Send null/empty parameters";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }

  [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
  [DefaultValue(false)]
  public object? Value { get; set; } = value;
}
