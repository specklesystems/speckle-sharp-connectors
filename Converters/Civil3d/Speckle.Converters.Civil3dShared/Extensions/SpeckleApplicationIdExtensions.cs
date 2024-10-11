namespace Speckle.Converters.Civil3dShared.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle object application id
  /// </summary>
  public static string GetSpeckleApplicationId(this CDB.Entity entity) => entity.Handle.Value.ToString();
}
