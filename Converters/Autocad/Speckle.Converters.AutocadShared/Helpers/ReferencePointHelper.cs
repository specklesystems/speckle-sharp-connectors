namespace Speckle.Converters.Autocad.Helpers;

/// <summary>
/// Helper class for working with reference points
/// </summary>
public static class ReferencePointHelper
{
  public const string REFERENCE_POINT_TRANSFORM_KEY = "referencePointTransform";

  /// <summary>
  /// Changes Autocad Matrix3d Transform to a double array.
  /// Uses a 16-element column-major matrix representation. See https://speckle.guide/dev/objects.html
  /// </summary>
  public static Dictionary<string, object> CreateTransformDataForRootObject(AG.Matrix3d transform)
  {
    return new Dictionary<string, object>
    {
      {
        "transform", // TODO: it would also be nice to include the key-value pair for reference point type as a string
        new[]
        {
          transform.CoordinateSystem3d.Xaxis.X,
          transform.CoordinateSystem3d.Xaxis.Y,
          transform.CoordinateSystem3d.Xaxis.Z,
          0,
          transform.CoordinateSystem3d.Yaxis.X,
          transform.CoordinateSystem3d.Yaxis.Y,
          transform.CoordinateSystem3d.Yaxis.Z,
          0,
          transform.CoordinateSystem3d.Zaxis.X,
          transform.CoordinateSystem3d.Zaxis.Y,
          transform.CoordinateSystem3d.Zaxis.Z,
          0,
          transform.CoordinateSystem3d.Origin.X,
          transform.CoordinateSystem3d.Origin.Y,
          transform.CoordinateSystem3d.Origin.Z,
          1
        }
      }
    };
  }
}
