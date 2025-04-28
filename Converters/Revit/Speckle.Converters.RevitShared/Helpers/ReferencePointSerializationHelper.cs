using Autodesk.Revit.DB;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Helper class for working with transform data coming from reference point setting
/// This allows preserving the reference point information between operations.
/// </summary>
public static class ReferencePointSerializationHelper
{
  // creating all these const probably not necessary?
  private const string ORIGIN_X = "originX";
  private const string ORIGIN_Y = "originY";
  private const string ORIGIN_Z = "originZ";

  private const string BASIS_XX = "basisXX";
  private const string BASIS_XY = "basisXY";
  private const string BASIS_XZ = "basisXZ";

  private const string BASIS_YX = "basisYX";
  private const string BASIS_YY = "basisYY";
  private const string BASIS_YZ = "basisYZ";

  private const string BASIS_ZX = "basisZX";
  private const string BASIS_ZY = "basisZY";
  private const string BASIS_ZZ = "basisZZ";

  /// <summary>
  /// "Serializes" a Revit Transform to a dictionary and attaches it to the root object.
  /// The transform represents the reference point setting on used on send
  /// </summary>
  /// <remarks>
  /// This allows models to maintain their spatial context when transferred between Revit instances
  /// with different reference point settings. If a model was created relative to Project Base or
  /// Survey Point, it will be properly positioned when received.
  /// </remarks>
  public static Dictionary<string, double> SerializeTransformToRootObject(Transform transform)
  {
    // best type for this? dict? Base?
    var transformData = new Dictionary<string, double>
    {
      // Origin components
      [ORIGIN_X] = transform.Origin.X,
      [ORIGIN_Y] = transform.Origin.Y,
      [ORIGIN_Z] = transform.Origin.Z,

      // BasisX components
      [BASIS_XX] = transform.BasisX.X,
      [BASIS_XY] = transform.BasisX.Y,
      [BASIS_XZ] = transform.BasisX.Z,

      // BasisY components
      [BASIS_YX] = transform.BasisY.X,
      [BASIS_YY] = transform.BasisY.Y,
      [BASIS_YZ] = transform.BasisY.Z,

      // BasisZ components
      [BASIS_ZX] = transform.BasisZ.X,
      [BASIS_ZY] = transform.BasisZ.Y,
      [BASIS_ZZ] = transform.BasisZ.Z
    };

    return transformData;
  }

  /// <summary>
  /// Extracts and reconstructs the transform from the dictionary stored in the root object
  /// </summary>
  public static Transform? DeserializeTransformFromRootObject(Dictionary<string, object> transformData)
  {
    // Extract origin
    XYZ origin =
      new(
        Convert.ToDouble(transformData[ORIGIN_X]),
        Convert.ToDouble(transformData[ORIGIN_Y]),
        Convert.ToDouble(transformData[ORIGIN_Z])
      );

    // Extract basis vectors
    XYZ basisX =
      new(
        Convert.ToDouble(transformData[BASIS_XX]),
        Convert.ToDouble(transformData[BASIS_XY]),
        Convert.ToDouble(transformData[BASIS_XZ])
      );

    XYZ basisY =
      new(
        Convert.ToDouble(transformData[BASIS_YX]),
        Convert.ToDouble(transformData[BASIS_YY]),
        Convert.ToDouble(transformData[BASIS_YZ])
      );

    XYZ basisZ =
      new(
        Convert.ToDouble(transformData[BASIS_ZX]),
        Convert.ToDouble(transformData[BASIS_ZY]),
        Convert.ToDouble(transformData[BASIS_ZZ])
      );

    // Create the transform
    Transform transform = Transform.Identity;
    transform.Origin = origin;
    transform.BasisX = basisX;
    transform.BasisY = basisY;
    transform.BasisZ = basisZ;

    return transform;
  }
}
