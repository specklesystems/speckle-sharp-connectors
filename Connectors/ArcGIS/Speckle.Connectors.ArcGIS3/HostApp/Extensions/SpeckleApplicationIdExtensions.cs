using ArcGIS.Core.Data.Raster;

namespace Speckle.Connectors.ArcGIS.HostApp.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle application id for map members
  /// </summary>
  public static string GetSpeckleApplicationId(this ADM.MapMember mapMember) => mapMember.URI;

  /// <summary>
  /// Constructs the Speckle application id for Features as a concatenation of the layer URI (applicationId)
  /// and the row OID (index of row in layer).
  /// </summary>
  /// <exception cref="ACD.Exceptions.GeodatabaseException">Throws when this is *not* called on MCT. Use QueuedTask.Run.</exception>
  public static string GetSpeckleApplicationId(this ACD.Row row, string layerApplicationId) =>
    $"{layerApplicationId}_{row.GetObjectID()}";

  /// <summary>
  /// Constructs the Speckle application id for Raster as a concatenation of the layer URI (applicationId) and 0-index
  /// </summary>
  public static string GetSpeckleApplicationId(this Raster _, string layerApplicationId) => $"{layerApplicationId}_0";

  /// <summary>
  /// Constructs the Speckle application id for LasDatasets as a concatenation of the layer URI (applicationId)
  /// and point OID.
  /// </summary>
  public static string GetSpeckleApplicationId(this ACD.Analyst3D.LasPoint point, string layerApplicationId) =>
    $"{layerApplicationId}_{point.PointID}";
}
