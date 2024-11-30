namespace Speckle.Converter.Navisworks.Helpers;

public static class GeometryHelpers
{
  /// <summary>
  /// Compares two vectors to determine if they are approximately equal within a given tolerance.
  /// </summary>
  /// <param name="vectorA">The first comparison vector.</param>
  /// <param name="vectorB">The second comparison vector.</param>
  /// <param name="tolerance">The tolerance value for the comparison. Default is 1e-9.</param>
  /// <returns>True if the vectors match within the tolerance; otherwise, false.</returns>
  public static bool VectorMatch(NAV.Vector3D vectorA, NAV.Vector3D vectorB, double tolerance = 1e-9) =>
    Math.Abs(vectorA.X - vectorB.X) < tolerance
    && Math.Abs(vectorA.Y - vectorB.Y) < tolerance
    && Math.Abs(vectorA.Z - vectorB.Z) < tolerance;
}
