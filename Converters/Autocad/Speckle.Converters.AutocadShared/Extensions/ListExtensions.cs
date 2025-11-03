namespace Speckle.Converters.Autocad.Extensions;

public static class ListExtensions
{
  public static List<AG.Point3d> ConvertToPoint3dFromWcsToOcs(this List<double> pointList, double conversionFactor = 1)
  {
    // throw if list is malformed
    if (pointList.Count % 3 != 0)
    {
      throw new ArgumentException("Point list of xyz values is malformed", nameof(pointList));
    }

    List<AG.Point3d> points3d = new(pointList.Count / 3);
    for (int i = 2; i < pointList.Count; i += 3)
    {
      points3d.Add(
        new AG.Point3d(
          pointList[i - 2] * conversionFactor,
          pointList[i - 1] * conversionFactor,
          pointList[i] * conversionFactor
        )
      );
    }

    return points3d;
  }

  /// <summary>
  /// Converts polyline vertex data to Point3d in OCS, supporting both 2D and 3D formats.
  /// 2D format: XY pairs already in OCS (from Point2d)
  /// 3D format: XYZ triplets in external coordinates that need OCS transformation
  /// </summary>
  public static List<AG.Point3d> ConvertPolylineValueToPoint3dInOcs(
    this List<double> pointList,
    AG.Vector3d normal,
    double elevation,
    double conversionFactor = 1
  )
  {
    bool is2DPointList = pointList.Count % 2 == 0 && pointList.Count % 3 != 0;

    if (is2DPointList)
    {
      // 2D format: XY pairs are already in OCS, just need to add elevation as Z
      List<AG.Point3d> points3d = new(pointList.Count / 2);
      for (int i = 0; i < pointList.Count; i += 2)
      {
        points3d.Add(
          new AG.Point3d(
            pointList[i] * conversionFactor,
            pointList[i + 1] * conversionFactor,
            elevation * conversionFactor // use the polyline's elevation
          )
        );
      }
      return points3d;
    }

    // 3D format: XYZ in external coords, transform to OCS
    return pointList.ConvertToPoint3dInOcs(normal, conversionFactor);
  }

  /// <summary>
  /// Converts a list of doubles to Point3d objects and transforms them to OCS (Object Coordinate System)
  /// based on the provided normal vector
  /// </summary>
  private static List<AG.Point3d> ConvertToPoint3dInOcs(
    this List<double> pointList,
    AG.Vector3d normal,
    double conversionFactor = 1
  )
  {
    AG.Matrix3d matrixOcs = AG.Matrix3d.WorldToPlane(normal);
    return pointList.ConvertToPoint3dFromWcsToOcs(conversionFactor).Select(p => p.TransformBy(matrixOcs)).ToList();
  }
}
