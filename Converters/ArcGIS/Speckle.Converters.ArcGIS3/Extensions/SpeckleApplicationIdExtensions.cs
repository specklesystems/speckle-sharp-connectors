namespace Speckle.Converters.ArcGIS3.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle application id for rows as a concatenation of the handle (generated from layer) and the row OID (index of row in layer)
  /// </summary>
  /// <exception cref="ACD.Exceptions.GeodatabaseException">Throws when this is *not* called on MCT. Use QueuedTask.Run.</exception>
  public static string GetSpeckleApplicationId(this ACD.Row row) => $"{row.Handle}_{row.GetObjectID()}";

  /// <summary>
  /// Retrieves the Speckle application id for las points as a concatenation of the handle (generated from layer) and the point OID (record number of point in las layer)
  /// </summary>
  /// <exception cref="ACD.Exceptions.GeodatabaseException">Throws when this is *not* called on MCT. Use QueuedTask.Run.</exception>
  public static string GetSpeckleApplicationId(this ACD.Analyst3D.LasPoint point) => $"{point.Handle}_{point.PointID}";

  /// <summary>
  /// Retrieves the Speckle application id for core objects bases as the handle (generated from layer)
  /// </summary>
  public static string GetSpeckleApplicationId(this AC.CoreObjectsBase coreObject) => $"{coreObject.Handle}";
}
