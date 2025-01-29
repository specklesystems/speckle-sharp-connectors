using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class IntervalToSpeckleConverter : ITypedConverter<RG.Interval, SOP.Interval>
{
  /// <summary>
  /// Converts a Rhino Interval object to a Speckle Interval object.
  /// </summary>
  /// <param name="target">The Rhino Interval object to be converted.</param>
  /// <returns>The converted Speckle Interval object.</returns>
  public SOP.Interval Convert(RG.Interval target) => new() { start = target.T0, end = target.T1 };
}
