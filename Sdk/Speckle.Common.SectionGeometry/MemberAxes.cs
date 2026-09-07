using Speckle.DoubleNumerics;

namespace Speckle.Common.SectionGeometry;

/// <summary>
/// The axis pair placing a section's (u, v) plane in the model.
/// </summary>
/// <remarks>
/// ETABS returns direction cosines from FrameObj.GetTransformationMatrix and needs none of the reconstruction below
/// - use the host's own answer, it has already resolved every edge case. TSD reports only a rotation angle on the
/// span, so the default frame has to be rebuilt, and it lives here so the two connectors can't drift apart on it.
/// </remarks>
public readonly struct MemberAxes
{
  /// <summary>Shorter than this and a direction vector isn't one.</summary>
  private const double MIN_LENGTH = 1e-9;

  /// <summary>Below this the member is too close to vertical for global Z to define its plane.</summary>
  private const double VERTICAL_TOLERANCE = 1e-6;

  /// <summary>Creates an axis pair directly, for hosts that report a full transformation.</summary>
  public MemberAxes(Vector3 widthAxis, Vector3 depthAxis)
  {
    WidthAxis = widthAxis;
    DepthAxis = depthAxis;
  }

  /// <summary>Runs along the section's width. Local 3 in CSi terms.</summary>
  public Vector3 WidthAxis { get; }

  /// <summary>Runs along the section's depth. Local 2 in CSi terms.</summary>
  public Vector3 DepthAxis { get; }

  /// <summary>Default axis pair for a member running in <paramref name="direction"/>, rolled about it.</summary>
  /// <param name="direction">Member direction, start toward end. Need not be normalised.</param>
  /// <param name="rollRadians">
  /// Rotation of the depth axis about the member, counter-clockwise about the positive member direction. This is
  /// CSi's Ang and TSD's RotationAngle.
  /// </param>
  /// <exception cref="ArgumentException">The direction has no length, so there's no plane to define.</exception>
  public static MemberAxes FromDirection(Vector3 direction, double rollRadians = 0)
  {
    double length = direction.Length();
    if (length < MIN_LENGTH)
    {
      throw new ArgumentException("A member with no length has no orientation.", nameof(direction));
    }

    Vector3 axial = direction / length;

    // depth wants to point up in the vertical plane holding the member, which is global Z projected onto the plane
    // perpendicular to it. for a vertical member that projection vanishes and both hosts fall back to global X.
    Vector3 projected = Vector3.UnitZ - (axial.Z * axial);
    Vector3 depthAxis = Vector3.Normalize(
      projected.Length() > VERTICAL_TOLERANCE ? projected : Vector3.UnitX - (axial.X * axial)
    );

    // rodrigues about the member. the axis is perpendicular to what it rotates, so the parallel term drops out
    depthAxis = Vector3.Normalize(
      (depthAxis * Math.Cos(rollRadians)) + (Vector3.Cross(axial, depthAxis) * Math.Sin(rollRadians))
    );

    // 3 = 1 × 2, matching CSi's right-handed local axes
    return new MemberAxes(Vector3.Cross(axial, depthAxis), depthAxis);
  }
}
