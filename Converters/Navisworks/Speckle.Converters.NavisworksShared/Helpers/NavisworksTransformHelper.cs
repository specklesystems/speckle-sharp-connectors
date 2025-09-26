using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Helpers;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// Helper class for extracting and working with Navisworks transforms.
/// </summary>
public static class NavisworksTransformHelper
{
  /// <summary>
  /// Extracts the transform matrix from a Navisworks instance item.
  /// </summary>
  /// <param name="instanceItem">The Navisworks instance item.</param>
  /// <returns>A transform matrix as a flat array or null if no transform.</returns>
  public static double[]? GetInstanceTransform(NAV.ModelItem instanceItem)
  {
    try
    {
      // Get the transform from the instance
      var transform = instanceItem.Transform;
      if (transform == null)
      {
        return null;
      }

      // Convert the Navisworks transform to a flat array
      var matrix = new double[16];
      matrix[0] = transform.M00; matrix[1] = transform.M01; matrix[2] = transform.M02; matrix[3] = transform.M03;
      matrix[4] = transform.M10; matrix[5] = transform.M11; matrix[6] = transform.M12; matrix[7] = transform.M13;
      matrix[8] = transform.M20; matrix[9] = transform.M21; matrix[10] = transform.M22; matrix[11] = transform.M23;
      matrix[12] = transform.M30; matrix[13] = transform.M31; matrix[14] = transform.M32; matrix[15] = transform.M33;

      return matrix;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // Note: We can't use logger here since this is a static method
      // The calling code should handle logging if needed
      return null;
    }
  }
}
