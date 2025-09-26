using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.DUI.Settings;

[GenerateAutoInterface]
public record CardSetting : ICardSetting
{
  public string? Id { get; set; }
  public string? Title { get; set; }
  public string? Type { get; set; }
  public object? Value { get; set; }
  public List<string>? Enum { get; set; }
}
