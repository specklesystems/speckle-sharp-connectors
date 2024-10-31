namespace Speckle.Connector.Tekla2024.Extensions;

public static class SpeckleApplicationIdExtensions
{
  public static string GetSpeckleApplicationId(this TSM.ModelObject modelObject) =>
    modelObject.Identifier.GUID.ToString();
}
