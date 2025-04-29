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
  public SA.Text Convert(RG.TextEntity target) =>
    new()
    {
      value = target.PlainText,
      height = target.TextHeight,
      maxWidth = target.FormatWidth == 0 ? null : target.FormatWidth,
      origin = _pointConverter.Convert(target.Plane.Origin),
      plane = GetTextPlane(target),
      alignmentH = SimplifyHorizontalAlignment((int)target.TextHorizontalAlignment),
      alignmentV = SimplifyVerticalAlignment((int)target.TextVerticalAlignment),
      units = _settingsStore.Current.SpeckleUnits
    };

  private SOG.Plane? GetTextPlane(RG.TextEntity target)
  {
    // set plane to null if text orientation follows camera view
    if (target.TextOrientation != TextOrientation.InPlane)
    {
      return null;
    }

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
  /// Simplify alignment from 4 to 3 options: Left (0, 3), Center (1), Right (2)
  /// </summary>
  private SA.AlignmentHorizontal SimplifyHorizontalAlignment(int alignment)
  {
    int simpleAlignment = alignment < 3 ? alignment : 0;
    return (SA.AlignmentHorizontal)simpleAlignment;
  }

  /// <summary>
  /// Simplify alignment from 5 to just 3 options: Top (0-2), Middle (3), Bottom (4-6)
  /// </summary>
  private SA.AlignmentVertical SimplifyVerticalAlignment(int alignment)
  {
    int simpleAlignment = alignment < 3 ? 0 : (alignment == 3 ? 1 : 2);
    return (SA.AlignmentVertical)simpleAlignment;
  }
}
