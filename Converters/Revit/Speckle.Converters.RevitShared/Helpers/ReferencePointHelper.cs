using Autodesk.Revit.DB;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Helper class for working with transform data coming from reference point setting
/// This allows preserving the reference point information between operations.
/// </summary>
public static class ReferencePointHelper
{
  public const string REFERENCE_POINT_TRANSFORM_KEY = "referencePointTransform";

  /// <summary>
  /// Changes Revit Transform to a double array.
  /// Uses a 16-element column-major matrix representation. See https://speckle.guide/dev/objects.html
  /// </summary>
  public static Dictionary<string, object> CreateTransformDataForRootObject(Transform transform)
  {
    return new Dictionary<string, object>
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
  }

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
