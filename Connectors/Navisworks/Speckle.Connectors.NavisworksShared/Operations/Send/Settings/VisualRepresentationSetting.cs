using Speckle.Connectors.DUI.Settings;
using Speckle.Converter.Navisworks.Models;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class VisualRepresentationSetting(RepresentationMode value) : ICardSetting
{
  public string? Id { get; set; } = "visualRepresentation";
  public string? Title { get; set; } = "Visual Representation";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(RepresentationMode)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, RepresentationMode> VisualRepresentationMap = System
    .Enum.GetValues(typeof(RepresentationMode))
    .Cast<RepresentationMode>()
    .ToDictionary(v => v.ToString(), v => v);
}
