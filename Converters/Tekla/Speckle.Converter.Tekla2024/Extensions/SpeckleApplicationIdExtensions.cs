namespace Speckle.Converter.Tekla2024.Extensions;

public static class SpeckleApplicationIdExtensions
{
  public static string GetSpeckleApplicationId(this TSM.ModelObject modelObject) =>
    modelObject.Identifier.GUID.ToString();

  public static string GetSpeckleApplicationId(this TSM.ControlObjectColorEnum controlColor) => $"color_{controlColor}";

  public static string GetSpeckleApplicationId(this TSM.ControlCircle.ControlCircleColorEnum controlColor) =>
    $"color_{controlColor}";

  public static string GetSpeckleApplicationId(this TSM.ControlLine.ControlLineColorEnum controlColor) =>
    $"color_{controlColor}";
}
