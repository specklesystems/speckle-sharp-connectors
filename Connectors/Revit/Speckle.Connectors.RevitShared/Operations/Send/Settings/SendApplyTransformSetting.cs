using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class SendApplyTransformSetting(bool value = SendApplyTransformSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "applyTransform";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Apply Transform";
  public string? Description { get; set; } =
    "Bakes the selected reference-point transform into converted geometry. Leave off to preserve Revit internal coordinates and store placement separately.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
