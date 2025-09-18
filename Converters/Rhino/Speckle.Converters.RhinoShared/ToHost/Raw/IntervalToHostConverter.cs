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
  /// <exception cref="ConversionException"> Throws when the Interval is null.</exception>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.Interval Convert([NotNull] SOP.Interval? target)
  {
    if (target == null)
    {
      // assuming user trying to receive old (v2) data without domains. Or other (non speckle-sharp) connectors aren't
      // enforcing domains (Sketchup, ArchiCAD)?
      throw new ConversionException("Cannot convert a null Interval.  Check your Rhino model.");
    }

    return new RG.Interval(target.start, target.end);
  }
}
