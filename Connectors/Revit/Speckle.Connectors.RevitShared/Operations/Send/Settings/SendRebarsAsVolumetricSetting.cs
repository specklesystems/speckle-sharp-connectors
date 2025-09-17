using System.ComponentModel;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.RevitShared.Operations;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendRebarsAsVolumetricSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = RevitSettingsConstants.SEND_REBARS_AS_VOLUMETRIC;
  public string? Title { get; set; } = "Send Rebars As Volumetric (disable for better performance)";
  public string? Type { get; set; } = "boolean";

  [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
  [DefaultValue(false)]
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
