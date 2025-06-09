using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class TextEntityToSpeckleConverter : ITypedConverter<RG.TextEntity, SA.Text>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public TextEntityToSpeckleConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino TextEntity to a Speckle Text object.
  /// </summary>
  /// <param name="target">The Rhino TextEntity to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public SA.Text Convert(RG.TextEntity target)
  {
    var plane = GetTextPlane(target, out bool screenOriented);

    return new SA.Text
    {
      value = target.PlainText,
      height = target.TextHeight * target.DimensionScale,
      maxWidth = target.FormatWidth == 0 ? null : target.FormatWidth,
      plane = plane,
      screenOriented = screenOriented,
      alignmentH = GetHorizontalAlignment(target.TextHorizontalAlignment),
      alignmentV = GetVerticalAlignment(target.TextVerticalAlignment),
      units = _settingsStore.Current.SpeckleUnits
    };
  }

  private SOG.Plane GetTextPlane(RG.TextEntity target, out bool screenOriented)
  {
    screenOriented = target.TextOrientation != TextOrientation.InPlane;

    if (target.TextRotationRadians == 0)
    {
      return _planeConverter.Convert(target.Plane);
    }
    // adjust text plane if rotation applied. Use a new plane to not modify existing object
    RG.Plane rotatedPlane =
      new()
      {
        Origin = target.Plane.Origin,
        XAxis = target.Plane.XAxis,
        YAxis = target.Plane.YAxis,
        ZAxis = target.Plane.ZAxis,
      };
    rotatedPlane.Rotate(target.TextRotationRadians, target.Plane.ZAxis);
    return _planeConverter.Convert(rotatedPlane);
  }

  /// <summary>
  /// Simplify horizontal text alignment to 3 options: Left, Center, Right
  /// </summary>
  private SA.AlignmentHorizontal GetHorizontalAlignment(TextHorizontalAlignment alignment)
  {
    return alignment switch
    {
      TextHorizontalAlignment.Left => SA.AlignmentHorizontal.Left,
      TextHorizontalAlignment.Center => SA.AlignmentHorizontal.Center,
      TextHorizontalAlignment.Right => SA.AlignmentHorizontal.Right,
      _ => SA.AlignmentHorizontal.Left, // .Auto alignment - only applies to Leaders that we don't support yet
    };
  }

  /// <summary>
  /// Simplify vertical text alignment to 3 options: Top, Middle, Bottom
  /// </summary>
  private SA.AlignmentVertical GetVerticalAlignment(TextVerticalAlignment alignment)
  {
    return alignment switch
    {
      TextVerticalAlignment.Top
      or TextVerticalAlignment.MiddleOfTop
      or TextVerticalAlignment.BottomOfTop
        => SA.AlignmentVertical.Top,
      TextVerticalAlignment.Middle => SA.AlignmentVertical.Center,
      TextVerticalAlignment.MiddleOfBottom
      or TextVerticalAlignment.Bottom
      or TextVerticalAlignment.BottomOfBoundingBox
        => SA.AlignmentVertical.Bottom,
      _ => SA.AlignmentVertical.Top, // .Auto alignment - only applies to Leaders that we don't support yet
    };
  }
}
