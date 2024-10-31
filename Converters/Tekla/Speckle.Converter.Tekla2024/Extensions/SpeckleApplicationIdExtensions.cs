namespace Speckle.Converter.Tekla2024.Extensions;

public static class SpeckleApplicationIdExtensions
{
  public static string GetSpeckleApplicationId(this TSM.ModelObject modelObject) =>
    modelObject.Identifier.GUID.ToString();

  public static string GetSpeckleApplicationId(this TSMUI.Color color) => $"color_{color.Red}_{color.Green}_{color.Blue}_{color.Transparency}";
}
