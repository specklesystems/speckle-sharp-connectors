using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class TextDotToSpeckleConverter : ITypedConverter<RG.TextDot, SO.Text>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;

  public TextDotToSpeckleConverter(ITypedConverter<RG.Point3d, SOG.Point> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  /// <summary>
  /// Converts a Rhino TextDot to a Speckle Text object.
  /// </summary>
  /// <param name="target">The Rhino TextDot to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public SO.Text Convert(RG.TextDot target) =>
    new()
    {
      value = target.Text,
      height = target.FontHeight,
      origin = _pointConverter.Convert(target.Point),
      alignmentH = 1,
      alignmentV = 1,
      // null to indicate use of screen units
      units = null
    };
}
