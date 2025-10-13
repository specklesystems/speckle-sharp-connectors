using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Operations.Receive.Settings;

public class ReceiveReferencePointSetting(ReceiveReferencePointType value = ReceiveReferencePointSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "referencePoint";
  public const ReceiveReferencePointType DEFAULT_VALUE = ReceiveReferencePointType.Source;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Reference Point";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(ReceiveReferencePointType)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, ReceiveReferencePointType> ReferencePointMap = System
    .Enum.GetValues(typeof(ReceiveReferencePointType))
    .Cast<ReceiveReferencePointType>()
    .ToDictionary(v => v.ToString(), v => v);
}
