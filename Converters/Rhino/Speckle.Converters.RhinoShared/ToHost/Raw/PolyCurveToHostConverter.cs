using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class PolyCurveToHostConverter : ITypedConverter<SOG.Polycurve, RG.PolyCurve>
{
  public ITypedConverter<ICurve, RG.Curve>? CurveConverter { get; set; } // POC: CNX-9311 Circular dependency injected by the container using property.

  private readonly ITypedConverter<SOP.Interval, RG.Interval> _intervalConverter;

  public PolyCurveToHostConverter(ITypedConverter<SOP.Interval, RG.Interval> intervalConverter)
  {
    _intervalConverter = intervalConverter;
  }

  /// <summary>
  /// Converts a SpecklePolyCurve object to a Rhino PolyCurve object.
  /// </summary>
  /// <param name="target">The SpecklePolyCurve object to convert.</param>
  /// <returns>The converted Rhino PolyCurve object.</returns>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public RG.PolyCurve Convert(SOG.Polycurve target)
  {
    RG.PolyCurve result = new();

    foreach (var segment in target.segments)
    {
      RG.Curve childCurve = CurveConverter.NotNull().Convert(segment);
      if (!childCurve.IsValid)
      {
        throw new ConversionException($"Failed to convert segment {segment}");
      }

      if (!result.AppendSegment(childCurve))
      {
        throw new ConversionException($"Failed to append segment {segment}");
      }
    }

    result.Domain = _intervalConverter.Convert(target.domain);

    return result;
  }
}
