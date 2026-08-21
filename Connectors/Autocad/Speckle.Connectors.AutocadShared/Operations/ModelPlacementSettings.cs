using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.Autocad;

namespace Speckle.Connectors.Autocad.Operations;

[SuppressMessage(
  "Usage",
  "CA2263:Prefer generic overload when type is known",
  Justification = "The connector targets both .NET Framework and modern .NET."
)]
public sealed class SendModelPlacementSetting(
  bool includeGridCoordinates = false,
  AutocadModelPlacement value = AutocadModelPlacement.DrawingWcs
) : ICardSetting
{
  public const string SETTING_ID = "referencePoint";
  public const AutocadModelPlacement DEFAULT_VALUE = AutocadModelPlacement.DrawingWcs;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Model Placement";
  public string? Description { get; set; } =
    "Selects the coordinate frame represented by the model. Civil 3D grid coordinates are available only when its local-to-grid operation is affine.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } =
    System
      .Enum.GetValues(typeof(AutocadModelPlacement))
      .Cast<AutocadModelPlacement>()
      .Where(x => includeGridCoordinates || x != AutocadModelPlacement.GridCoordinates)
      .Select(x => x.ToString())
      .ToList();
  public object? Value { get; set; } = value.ToString();
}

public sealed class SendApplyTransformSetting(bool value = SendApplyTransformSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "applyTransform";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Apply Transform";
  public string? Description { get; set; } =
    "Bakes the selected model-placement transform into converted geometry. Leave off to preserve drawing WCS coordinates and store placement separately.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}

public sealed class ReceiveApplyTransformSetting(bool value = ReceiveApplyTransformSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "applyTransform";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Apply Transform";
  public string? Description { get; set; } =
    "Bakes the source model's stored placement transform while receiving. Leave off to preserve its stored coordinates.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}

public static class ModelPlacementSettings
{
  public static AutocadModelPlacement GetSendPlacement(SenderModelCard card, bool includeGridCoordinates)
  {
    string? raw = card.Settings?.FirstOrDefault(x => x.Id == SendModelPlacementSetting.SETTING_ID)?.Value as string;
    if (
      System.Enum.TryParse(raw, out AutocadModelPlacement placement)
      && (includeGridCoordinates || placement != AutocadModelPlacement.GridCoordinates)
    )
    {
      return placement;
    }
    return SendModelPlacementSetting.DEFAULT_VALUE;
  }

  public static bool GetSendApplyTransform(SenderModelCard card) =>
    card.Settings?.FirstOrDefault(x => x.Id == SendApplyTransformSetting.SETTING_ID)?.Value as bool?
    ?? SendApplyTransformSetting.DEFAULT_VALUE;

  public static bool GetReceiveApplyTransform(ModelCard card) =>
    card.Settings?.FirstOrDefault(x => x.Id == ReceiveApplyTransformSetting.SETTING_ID)?.Value as bool?
    ?? ReceiveApplyTransformSetting.DEFAULT_VALUE;
}
