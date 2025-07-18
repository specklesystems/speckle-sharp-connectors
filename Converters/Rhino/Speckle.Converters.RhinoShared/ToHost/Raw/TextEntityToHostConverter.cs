using Rhino.DocObjects;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class TextEntityToHostConverter : ITypedConverter<SA.Text, RG.TextEntity>
{
  private readonly ITypedConverter<SOG.Point, RG.Point3d> _pointConverter;
  private readonly ITypedConverter<SOG.Plane, RG.Plane> _planeConverter;
  public TextEntityToHostConverter(
    ITypedConverter<SOG.Point, RG.Point3d> pointConverter,
    ITypedConverter<SOG.Plane, RG.Plane> planeConverter
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
  }
  public RG.TextEntity Convert(SA.Text target) => new RG.TextEntity
  {
    Plane = _planeConverter.Convert(target.plane),
    PlainText = target.value,
    TextHeight = target.height,
    FormatWidth = target.maxWidth ?? 0,

    TextOrientation = target.screenOriented ? TextOrientation.InView : TextOrientation.InPlane,

    DimensionScale = 1.0, // Assuming no scaling is applied
    TextHorizontalAlignment = GetTextHorizontalAlignment(target.alignmentH),
    TextVerticalAlignment = GetTextVerticalAlignment(target.alignmentV),
  };

  private TextHorizontalAlignment GetTextHorizontalAlignment(
    SA.AlignmentHorizontal alignment
  )
  {
    return alignment switch
    {
      SA.AlignmentHorizontal.Left => TextHorizontalAlignment.Left,
      SA.AlignmentHorizontal.Center => TextHorizontalAlignment.Center,
      SA.AlignmentHorizontal.Right => TextHorizontalAlignment.Right,
      _ => TextHorizontalAlignment.Left // Default to Left if unknown
    };
  }
  private TextVerticalAlignment GetTextVerticalAlignment(
    SA.AlignmentVertical alignment
  )
  {
    return alignment switch
    {
      SA.AlignmentVertical.Top => TextVerticalAlignment.Top,
      SA.AlignmentVertical.Center=> TextVerticalAlignment.Middle,
      SA.AlignmentVertical.Bottom => TextVerticalAlignment.Bottom,
      _ => TextVerticalAlignment.Middle // Default to Middle if unknown
    };
  }
}
