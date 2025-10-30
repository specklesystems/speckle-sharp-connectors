namespace Speckle.Converters.Autocad;

public interface IReferencePointConverter
{
  /// <summary>
  /// Converts a list of doubles representing point3ds in WCS coordinates to the current active coordinate system
  /// </summary>
  /// <param name="d"></param>
  /// <returns></returns>
  List<double> ConvertWCSDoublesToExternalCoordinates(List<double> d);

  /// <summary>
  /// Converts a Point in WCS coordinates to the current active coordinate system
  /// </summary>
  /// <param name="p"></param>
  /// <returns></returns>
  AG.Point3d ConvertWCSPointToExternalCoordinates(AG.Point3d p);

  /// <summary>
  /// Converts a Vector in WCS coordinates to the current active coordinate system
  /// </summary>
  /// <param name="v"></param>
  /// <returns></returns>
  AG.Vector3d ConvertWCSVectorToExternalCoordinates(AG.Vector3d v);
}
