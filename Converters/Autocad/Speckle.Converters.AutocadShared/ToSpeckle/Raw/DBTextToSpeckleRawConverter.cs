using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Annotation;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBTextToSpeckleRawConverter : ITypedConverter<ADB.DBText, Text>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBTextToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts AutoCAD DBText to a Speckle Text object.
  /// </summary>
  /// <param name="target">The AutoCAD DBText to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public Text Convert(ADB.DBText target) =>
    // target.WidthFactor is ignored, because we don't support 1-dimensional text scaling. Needs to be added as Transforms
    // AlignmentPoint can be ignored, as it results in a Rotation
    new()
    {
      value = target.TextString,
      height = target.Height,
      maxWidth = null,
      origin = _pointConverter.Convert(target.Position),
      plane = GetTextPlane(target),
      alignmentH = SimplifyHorizontalAlignment(target.Justify),
      alignmentV = SimplifyVerticalAlignment(target.Justify),
      units = _settingsStore.Current.SpeckleUnits
    };

  private SOG.Plane? GetTextPlane(ADB.DBText target)
  {
    AG.Plane plane = new(target.Position, target.Normal);

    if (target.Rotation != 0)
    {
      plane.RotateBy(target.Rotation, target.Normal, target.Position);
    }

    return _planeConverter.Convert(plane);
  }

  /// <summary>
  /// Simplify alignment from 4 to 3 options: Left (0, 3), Center (1), Right (2)
  /// </summary>
  private SA.AlignmentHorizontal SimplifyHorizontalAlignment(ADB.AttachmentPoint attachPoint)
  {
    // Bottom Center: BaseMid
    if (attachPoint == ADB.AttachmentPoint.BaseMid)
    {
      return SA.AlignmentHorizontal.Center;
    }

    // Remaining types to default to .Left: Bottom Left (BaseAlign, BaseFit, BaseLeft), Middle Left (BaseMid), Top Left ()
    return SA.AlignmentHorizontal.Left;
  }

  /// <summary>
  /// Simplify alignment from 5 to just 3 options: Top (0-2), Middle (3), Bottom (4-6)
  /// </summary>
  private SA.AlignmentVertical SimplifyVerticalAlignment(ADB.AttachmentPoint attachPoint)
  {
    //
    if (attachPoint == ADB.AttachmentPoint.BaseMid)
    {
      return SA.AlignmentVertical.Center;
    }
    return SA.AlignmentVertical.Bottom;
  }
}
