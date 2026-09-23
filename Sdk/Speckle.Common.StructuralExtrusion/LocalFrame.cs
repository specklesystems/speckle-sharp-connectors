using Speckle.DoubleNumerics;

namespace Speckle.Common.StructuralExtrusion;

/// <summary>
/// Right-handed placement frame (X × Y = Z). Z is the extrusion axis (local 1), X the profile's depth axis
/// (local 2), Y its width axis (local 3). A positive rotation turns X towards Y about Z, which is the CSi
/// local-axis-angle convention.
/// </summary>
public readonly record struct LocalFrame(Vector3 XAxis, Vector3 YAxis, Vector3 ZAxis)
{
  private const double PARALLEL_EPSILON = 1e-12;

  /// <summary>Null when <paramref name="zAxis"/> is degenerate or parallel to <paramref name="xReference"/>.</summary>
  public static LocalFrame? TryCreate(Vector3 zAxis, Vector3 xReference, double rotationRadians)
  {
    if (zAxis.LengthSquared() < PARALLEL_EPSILON)
    {
      return null;
    }

    var z = Vector3.Normalize(zAxis);
    var x = xReference - Vector3.Dot(xReference, z) * z;
    if (x.LengthSquared() < PARALLEL_EPSILON)
    {
      return null;
    }
    x = Vector3.Normalize(x);
    var y = Vector3.Cross(z, x);

    if (rotationRadians != 0)
    {
      double c = Math.Cos(rotationRadians);
      double s = Math.Sin(rotationRadians);
      var rotatedX = c * x + s * y;
      var rotatedY = -s * x + c * y;
      x = rotatedX;
      y = rotatedY;
    }

    return new LocalFrame(x, y, z);
  }

  public Vector3 ToWorld(Vector3 origin, double x, double y, double z) => origin + x * XAxis + y * YAxis + z * ZAxis;
}
