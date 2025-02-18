using Rhino.Collections;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public interface IFlatPointListToHostConverter : ITypedConverter<IReadOnlyList<double>, Point3dList>
{
  IEnumerable<RG.Point3d> ConvertToEnum(IReadOnlyList<double> target);
}

/// <summary>
/// Converts a flat list of raw double values to a Point3dList.
/// </summary>
public class FlatPointListToHostConverter : IFlatPointListToHostConverter
{
  /// <summary>
  /// Converts a flat list of raw double values to a Point3dList.
  /// </summary>
  /// <param name="target">The flat list of raw double values</param>
  /// <returns>A Point3dList object that represents the converted points</returns>
  /// <remarks>
  /// Assumes that the amount of numbers contained on the list is a multiple of 3,
  /// with the numbers being coordinates of each point in the format {x1, y1, z1, x2, y2, z2, ..., xN, yN, zN}
  /// </remarks>
  /// <exception cref="SpeckleException">Throws when the input list count is not a multiple of 3.</exception>
  public Point3dList Convert(IReadOnlyList<double> target) => new(ConvertToEnum(target));

  public IEnumerable<RG.Point3d> ConvertToEnum(IReadOnlyList<double> target)
  {
    if (target.Count % 3 != 0)
    {
      throw new ValidationException("Array malformed: length%3 != 0.");
    }

    for (int i = 2; i < target.Count; i += 3)
    {
      yield return new RG.Point3d(target[i - 2], target[i - 1], target[i]);
    }
  }
}
