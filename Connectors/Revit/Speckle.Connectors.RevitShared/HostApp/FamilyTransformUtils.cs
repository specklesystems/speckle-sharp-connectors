using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.DoubleNumerics;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Pure math and transform utilities for Revit Family Instance placement.
/// </summary>
/// <remarks>
/// Architecturally, these methods live in the Connector rather than the Converters project
/// because stripping scale/skew and applying API mirroring are Host App-specific workarounds
/// for Revit's shitty Family Instance rules, not general stateless conversion logic.
/// </remarks>
public static class FamilyTransformUtils
{
  /// <summary>
  /// Checks if a transform matrix contains non-uniform scaling or shear/skew.
  /// </summary>
  /// <remarks>
  /// Revit family instances natively reject matrices with scale or skew.
  /// We flag these matrices so we can sanitize them before placement.
  /// </remarks>
  public static bool HasScaleOrSkew(Matrix4x4 matrix)
  {
    // Extract lengths of basis vectors
    var lenX = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M21 * matrix.M21 + matrix.M31 * matrix.M31);
    var lenY = Math.Sqrt(matrix.M12 * matrix.M12 + matrix.M22 * matrix.M22 + matrix.M32 * matrix.M32);
    var lenZ = Math.Sqrt(matrix.M13 * matrix.M13 + matrix.M23 * matrix.M23 + matrix.M33 * matrix.M33);

    // Calculate dot products to check for orthogonality
    var dotXy = matrix.M11 * matrix.M12 + matrix.M21 * matrix.M22 + matrix.M31 * matrix.M32;
    var dotXz = matrix.M11 * matrix.M13 + matrix.M21 * matrix.M23 + matrix.M31 * matrix.M33;
    var dotYz = matrix.M12 * matrix.M13 + matrix.M22 * matrix.M23 + matrix.M32 * matrix.M33;

    double tol = 1e-4;

    bool isOrthogonal = Math.Abs(dotXy) < tol && Math.Abs(dotXz) < tol && Math.Abs(dotYz) < tol;
    bool isUnitScale = Math.Abs(lenX - 1.0) < tol && Math.Abs(lenY - 1.0) < tol && Math.Abs(lenZ - 1.0) < tol;

    return !isOrthogonal || !isUnitScale;
  }

  /// <summary>
  /// Sanitizes a transform matrix by stripping out any scale or skew, returning a rigid transform.
  /// </summary>
  public static Matrix4x4 RemoveScaleAndSkew(Matrix4x4 matrix)
  {
    // 1. Extract Z column and normalize
    double zX = matrix.M13,
      zY = matrix.M23,
      zZ = matrix.M33;
    double lenZ = Math.Sqrt(zX * zX + zY * zY + zZ * zZ);
    if (lenZ > 1e-6)
    {
      zX /= lenZ;
      zY /= lenZ;
      zZ /= lenZ;
    }
    else
    {
      zX = 0;
      zY = 0;
      zZ = 1;
    }

    // 2. Extract Y column
    double yX = matrix.M12,
      yY = matrix.M22,
      yZ = matrix.M32;

    // 3. Cross product Y and Z to get orthogonal X
    double xX = yY * zZ - yZ * zY;
    double xY = yZ * zX - yX * zZ;
    double xZ = yX * zY - yY * zX;
    double lenX = Math.Sqrt(xX * xX + xY * xY + xZ * xZ);
    if (lenX > 1e-6)
    {
      xX /= lenX;
      xY /= lenX;
      xZ /= lenX;
    }
    else
    {
      xX = 1;
      xY = 0;
      xZ = 0;
    }

    // 4. Cross product Z and X to get orthogonal unit Y
    yX = zY * xZ - zZ * xY;
    yY = zZ * xX - zX * xZ;
    yZ = zX * xY - zY * xX;
    double lenY = Math.Sqrt(yX * yX + yY * yY + yZ * yZ);
    if (lenY > 1e-6)
    {
      yX /= lenY;
      yY /= lenY;
      yZ /= lenY;
    }

    return new Matrix4x4(
      xX,
      yX,
      zX,
      matrix.M14,
      xY,
      yY,
      zY,
      matrix.M24,
      xZ,
      yZ,
      zZ,
      matrix.M34,
      matrix.M41,
      matrix.M42,
      matrix.M43,
      matrix.M44
    );
  }

  /// <summary>
  /// Evaluates the determinant of a matrix to check if it encodes a mirrored state.
  /// </summary>
  /// <remarks>
  /// A negative determinant implies a left-handed coordinate system (mirroring).
  /// We extract this so we can apply the mirror via Revit's native API instead.
  /// </remarks>
  public static (bool X, bool Y, bool Z) GetMirrorState(Matrix4x4 matrix)
  {
    var det =
      matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32)
      - matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31)
      + matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);

    return det < 0 ? (true, false, false) : (false, false, false);
  }

  /// <summary>
  /// Applies native Revit mirror operations to an element based on the evaluated mirror state.
  /// </summary>
  /// <remarks>
  /// Because we strip the mirrored (left-handed) state from the initial transform to keep Revit happy,
  /// we must restore the mirrored geometry as a post-placement operation.
  /// </remarks>
  public static void ApplyMirroring(
    Document document,
    ElementId elementId,
    Autodesk.Revit.DB.Plane plane,
    (bool X, bool Y, bool Z) mirrorState,
    ILogger logger
  )
  {
    var mirrorOperations = new List<(string name, bool shouldMirror, Autodesk.Revit.DB.Plane mirrorPlane)>
    {
      ("YZ", mirrorState.X, Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.YVec, plane.Normal)),
      ("XZ", mirrorState.Y, Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.Normal)),
      ("XY", mirrorState.Z, Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.YVec))
    };

    foreach (var (name, _, mirrorPlane) in mirrorOperations.Where(op => op.shouldMirror))
    {
      try
      {
        document.Regenerate();
        ElementTransformUtils.MirrorElements(document, [elementId], mirrorPlane, false);
      }
      catch (Autodesk.Revit.Exceptions.ApplicationException e)
      {
        logger.LogWarning(e, "Failed to mirror element on {PlaneName} plane", name);
      }
      finally
      {
        mirrorPlane.Dispose();
      }
    }
  }
}
