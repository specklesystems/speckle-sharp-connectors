using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Receive.Settings;

public class ConvertMeshesToPolysurfacesSetting(bool value = false) : ICardSetting
{
  public const string SETTING_ID = "convertMeshesToPolysurfaces";

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Convert meshes to polysurfaces";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
