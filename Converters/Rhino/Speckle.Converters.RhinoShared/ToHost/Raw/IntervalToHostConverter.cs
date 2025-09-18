using System.Diagnostics.CodeAnalysis;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

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
  public RG.Interval Convert([NotNull] SOP.Interval? target)
  {
    if (target == null)
    {
      throw new ConversionException("Cannot convert a null Interval.  Check your Rhino model.");
    }

    return new RG.Interval(target.start, target.end);
  }
}
