using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.CSiShared.Settings;

public class SendVolumetricGeometrySetting(bool value = SendVolumetricGeometrySetting.DEFAULT_VALUE) : ICardSetting
{
  public const string SETTING_ID = "sendVolumetricGeometry";
  public const bool DEFAULT_VALUE = false;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Send Volumetric Geometry (Beta)";
  public string? Description { get; set; } =
    "Extrudes frames, walls and floors into solid meshes from their section and thickness instead of lines and flat surfaces, coloured by material. Beta: elements with unsupported sections keep their wireframe representation.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
