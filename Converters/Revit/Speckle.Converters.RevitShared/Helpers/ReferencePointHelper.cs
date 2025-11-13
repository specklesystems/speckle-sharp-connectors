using Autodesk.Revit.DB;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Helper class for working with transform data coming from reference point setting
/// This allows preserving the reference point information between operations.
/// </summary>
public static class ReferencePointHelper
{
  /// <summary>
  /// Changes Revit Transform to a double array.
  /// Uses a 16-element column-major matrix representation. See https://speckle.guide/dev/objects.html
  /// </summary>
  public static Dictionary<string, object> CreateTransformDataForRootObject(Transform transform) =>
    new()
    {
      {
        "transform", // TODO: it would also be nice to include the key-value pair for reference point type as a string
        new[]
        {
          transform.BasisX.X,
          transform.BasisX.Y,
          transform.BasisX.Z,
          0,
          transform.BasisY.X,
          transform.BasisY.Y,
          transform.BasisY.Z,
          0,
          transform.BasisZ.X,
          transform.BasisZ.Y,
          transform.BasisZ.Z,
          0,
          transform.Origin.X,
          transform.Origin.Y,
          transform.Origin.Z,
          1
        }
      }
    };

  public static Matrix4x4 TransformToMatrix(Transform transform) =>
    new()
    {
      M11 = transform.BasisX.X,
      M21 = transform.BasisX.Y,
      M31 = transform.BasisX.Z,
      M41 = 0,

      M12 = transform.BasisY.X,
      M22 = transform.BasisY.Y,
      M32 = transform.BasisY.Z,
      M42 = 0,

      M13 = transform.BasisZ.X,
      M23 = transform.BasisZ.Y,
      M33 = transform.BasisZ.Z,
      M43 = 0,

      M14 = transform.Origin.X,
      M24 = transform.Origin.Y,
      M34 = transform.Origin.Z,
      M44 = 1
    };

  /// <summary>
  /// Extracts and reconstructs a transform from the matrix data stored on root object
  /// </summary>
  public static Transform? GetTransformFromRootObject(object? matrixDataObj)
  {
    double[]? matrixData = null;

    // NOTE: why all these if checks? We send double[] but get List<object> back on receive, so need to convert
    if (matrixDataObj is double[] doubleArray)
    {
      matrixData = doubleArray;
    }
    else if (matrixDataObj is List<object> listValues)
    {
      matrixData = listValues.Select(v => Convert.ToDouble(v)).ToArray();
    }

    if (matrixData == null || matrixData.Length != 16)
    {
      return null;
    }

    // Extract components from the matrix
    XYZ basisX = new(matrixData[0], matrixData[1], matrixData[2]);
    XYZ basisY = new(matrixData[4], matrixData[5], matrixData[6]);
    XYZ basisZ = new(matrixData[8], matrixData[9], matrixData[10]);
    XYZ origin = new(matrixData[12], matrixData[13], matrixData[14]);

    Transform transform = Transform.Identity;
    transform.Origin = origin;
    transform.BasisX = basisX;
    transform.BasisY = basisY;
    transform.BasisZ = basisZ;

    return transform;
  }
}
