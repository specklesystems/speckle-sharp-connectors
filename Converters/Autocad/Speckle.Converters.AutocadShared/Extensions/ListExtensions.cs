namespace Speckle.Converters.Autocad.Extensions;

public static class ListExtensions
{
  public static List<AG.Point3d> ConvertToPoint3d(this List<double> pointList, double conversionFactor = 1)
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
  /// Converts a list of doubles to Point3d objects and transforms them to OCS (Object Coordinate System)
  /// based on the provided normal vector
  /// </summary>
  public static List<AG.Point3d> ConvertToPoint3dInOcs(
    this List<double> pointList,
    AG.Vector3d normal,
    double conversionFactor = 1
  )
  {
    AG.Matrix3d matrixOcs = AG.Matrix3d.WorldToPlane(normal);
    return pointList.ConvertToPoint3d(conversionFactor).Select(p => p.TransformBy(matrixOcs)).ToList();
  }
}
