using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Annotation;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class MTextToSpeckleRawConverter : ITypedConverter<ADB.MText, Text>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public MTextToSpeckleRawConverter(
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
  /// Converts AutoCAD MText to a Speckle Text object.
  /// </summary>
  /// <param name="target">The AutoCAD MText to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public Text Convert(ADB.MText target) =>
    new()
    {
      value = target.Text,
      height = target.TextHeight,
      maxWidth = target.Width,
      origin = _pointConverter.Convert(target.Location),
      plane = GetTextPlane(target),
      alignmentH = GetHorizontalAlignment(target.Attachment),
      alignmentV = GetVerticalAlignment(target.Attachment),
      units = _settingsStore.Current.SpeckleUnits
    };

  private SOG.Plane? GetTextPlane(ADB.MText target)
  {
    AG.Plane plane = new(target.Location, target.Normal);

    if (target.Rotation != 0)
    {
      plane.RotateBy(target.Rotation, target.Normal, target.Location);
    }

    return _planeConverter.Convert(plane);
  }

  /// <summary>
  /// Simplify horizontal text alignment to 3 options: Left, Center, Right
  /// </summary>
  private SA.AlignmentHorizontal GetHorizontalAlignment(ADB.AttachmentPoint attachmentPt)
  {
    return attachmentPt switch
    {
      ADB.AttachmentPoint.TopLeft
      or ADB.AttachmentPoint.MiddleLeft
      or ADB.AttachmentPoint.BottomLeft
      or ADB.AttachmentPoint.BaseLeft
        => SA.AlignmentHorizontal.Left,
      ADB.AttachmentPoint.TopCenter
      or ADB.AttachmentPoint.MiddleCenter
      or ADB.AttachmentPoint.BottomCenter
      or ADB.AttachmentPoint.BaseCenter
        => SA.AlignmentHorizontal.Center,
      ADB.AttachmentPoint.TopRight
      or ADB.AttachmentPoint.MiddleRight
      or ADB.AttachmentPoint.BottomRight
      or ADB.AttachmentPoint.BaseRight
        => SA.AlignmentHorizontal.Right,
      _ => SA.AlignmentHorizontal.Left,
    };
  }

  /// <summary>
  /// Simplify vertical text alignment to 3 options: Top, Middle, Bottom
  /// </summary>
  private SA.AlignmentVertical GetVerticalAlignment(ADB.AttachmentPoint attachmentPt)
  {
    return attachmentPt switch
    {
      ADB.AttachmentPoint.TopLeft
      or ADB.AttachmentPoint.TopCenter
      or ADB.AttachmentPoint.TopRight
      or ADB.AttachmentPoint.TopMid
      or ADB.AttachmentPoint.TopAlign
      or ADB.AttachmentPoint.TopFit
        => SA.AlignmentVertical.Top,
      ADB.AttachmentPoint.MiddleLeft
      or ADB.AttachmentPoint.MiddleCenter
      or ADB.AttachmentPoint.MiddleRight
      or ADB.AttachmentPoint.MiddleAlign
      or ADB.AttachmentPoint.MiddleFit
      or ADB.AttachmentPoint.MiddleMid
        => SA.AlignmentVertical.Center,
      ADB.AttachmentPoint.BottomLeft
      or ADB.AttachmentPoint.BottomCenter
      or ADB.AttachmentPoint.BottomRight
      or ADB.AttachmentPoint.BottomAlign
      or ADB.AttachmentPoint.BottomFit
      or ADB.AttachmentPoint.BottomMid
        => SA.AlignmentVertical.Bottom,
      _ => SA.AlignmentVertical.Top,
    };
  }
}
