using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// "Include reference attachments" send setting: when on (the default), elements of every displayed
/// reference attachment are sent in the master model's frame — the interactive counterpart of
/// dgnextract's attachment-centric view-group walk (ENG-8749).
/// </summary>
public sealed class SendIncludeReferencesSetting(bool value = SendIncludeReferencesSetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "includeReferenceAttachments";
  public const bool DEFAULT_VALUE = true;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Include reference attachments";
  public string? Description { get; set; } =
    "Also publish the elements of displayed reference attachments, placed in the active model's frame.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}

public static class MicroStationSendSettings
{
  public static bool GetIncludeReferences(SenderModelCard card) =>
    card.Settings?.FirstOrDefault(x => x.Id == SendIncludeReferencesSetting.SETTING_ID)?.Value as bool?
    ?? SendIncludeReferencesSetting.DEFAULT_VALUE;
}
