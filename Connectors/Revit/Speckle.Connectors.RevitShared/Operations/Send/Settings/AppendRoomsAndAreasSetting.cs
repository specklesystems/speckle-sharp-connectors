using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public enum AppendRoomsAndAreasMode
{
  None,
  RoomsOnly,
  AreasOnly,
  Both,
}

[SuppressMessage(
  "Usage",
  "CA2263:Prefer generic overload when type is known",
  Justification = "Multi-targeting friction"
)]
public class AppendRoomsAndAreasSetting(AppendRoomsAndAreasMode value = AppendRoomsAndAreasSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "appendRoomsAndAreas";
  public const AppendRoomsAndAreasMode DEFAULT_VALUE = AppendRoomsAndAreasMode.None;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Append Rooms and Areas";
  public string? Type { get; set; } = "string";
  public List<string>? Enum { get; set; } = System.Enum.GetNames(typeof(AppendRoomsAndAreasMode)).ToList();
  public object? Value { get; set; } = value.ToString();

  public static readonly Dictionary<string, AppendRoomsAndAreasMode> AppendRoomsAndAreasMap = System
    .Enum.GetValues(typeof(AppendRoomsAndAreasMode))
    .Cast<AppendRoomsAndAreasMode>()
    .ToDictionary(v => v.ToString(), v => v);
}
