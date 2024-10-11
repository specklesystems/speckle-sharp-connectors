namespace Speckle.Converters.Civil3dShared.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle object application id
  /// </summary>
  public static string GetSpeckleApplicationId(this CDB.Entity entity) => entity.Handle.Value.ToString();

  /// <summary>
  /// Retrieves the Speckle application id from an ObjectId.
  /// This is used primarily when storing civil entity relationships, eg alignments used for sites and corridors
  /// </summary>
  /// <param name="objectId"></param>
  /// <returns></returns>
  public static string GetSpeckleApplicationId(this ADB.ObjectId objectId) => objectId.Handle.Value.ToString();
}
