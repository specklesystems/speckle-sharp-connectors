using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class SendVertexNormals(bool value) : ICardSetting
{
  public string? Id { get; set; } = "sendVertexNormals";
  public string? Title { get; set; } = "Send Vertex Normals";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}
