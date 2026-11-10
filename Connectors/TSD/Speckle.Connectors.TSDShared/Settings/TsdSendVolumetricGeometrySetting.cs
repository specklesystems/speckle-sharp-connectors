using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.TSDShared.Settings;

internal sealed class TsdSendVolumetricGeometrySetting(bool value = TsdSendVolumetricGeometrySetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "sendVolumetricGeometry";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Send Volumetric Geometry (Beta)";
  public string? Description { get; set; } =
    "Extrudes members, walls and slabs into solid meshes from their section and thickness instead of lines and flat surfaces. Beta: elements with unsupported sections keep their wireframe representation.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
