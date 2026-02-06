using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Receive.Settings;

public class ConvertMeshesToBrepsSetting(bool value = false) : ICardSetting
{
  public const string SETTING_ID = "convertMeshesToBreps";

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Convert solid meshes to Breps";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
