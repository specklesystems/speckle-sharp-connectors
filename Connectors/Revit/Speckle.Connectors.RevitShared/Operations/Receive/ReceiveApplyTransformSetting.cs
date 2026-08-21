using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class ReceiveApplyTransformSetting(bool value = ReceiveApplyTransformSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "applyTransform";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Apply Transform";
  public string? Description { get; set; } =
    "Bakes the selected reference-point transform while converting received geometry. Leave off to preserve the stored coordinates.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
