using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class IntervalToHostConverter : ITypedConverter<SOP.Interval, RG.Interval>
{
  /// <summary>
  /// Converts a Speckle Interval object to a Rhino.Geometry.Interval object.
  /// </summary>
  /// <param name="target">The Speckle Interval to convert.</param>
  /// <returns>The converted Rhino.Geometry.Interval object.</returns>
  /// <exception cref="ArgumentException">Thrown when the start or end value of the Interval is null.</exception>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.Interval Convert(SOP.Interval? target)
  {
    if (target == null)
    {
      return RG.Interval.Unset;
    }

    return new RG.Interval(target.start, target.end);
  }
}
