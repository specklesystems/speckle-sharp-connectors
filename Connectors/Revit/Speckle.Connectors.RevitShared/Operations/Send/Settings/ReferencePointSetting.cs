using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class ReferencePointSetting(ReferencePointType value) : ICardSetting
{
  public string? Id { get; set; } = "referencePoint";
  public string? Title { get; set; } = "Reference Point";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(ReferencePointType)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, ReferencePointType> ReferencePointMap = System
    .Enum.GetValues(typeof(ReferencePointType))
    .Cast<ReferencePointType>()
    .ToDictionary(v => v.ToString(), v => v);
}
