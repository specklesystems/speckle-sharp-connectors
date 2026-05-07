using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class OriginModeSetting(OriginMode value) : ICardSetting
{
  public string? Id { get; set; } = "originMode";
  public string? Title { get; set; } = "Origin Mode";
  public string? Description { get; set; } = "The point used as the origin of the sent geometry.";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(OriginMode)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, OriginMode> OriginModeMap = System
    .Enum.GetValues(typeof(OriginMode))
    .Cast<OriginMode>()
    .ToDictionary(v => v.ToString(), v => v);
}
