using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class MTextToSpeckleRawConverter : ITypedConverter<ADB.MText, SA.Text>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public MTextToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts AutoCAD MText to a Speckle Text object.
  /// </summary>
  /// <param name="target">The AutoCAD MText to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public SA.Text Convert(ADB.MText target) =>
    new()
    {
      value = target.Text,
      height = target.TextHeight,
      maxWidth = target.Width,
      plane = GetTextPlane(target),
      screenOriented = false,
      alignmentH = GetHorizontalAlignment(target.Attachment),
      alignmentV = GetVerticalAlignment(target.Attachment),
      units = _settingsStore.Current.SpeckleUnits
    };

  // For MText, the following properties are stored in:
  // - Position: WCS
  // - Normal: WCS??
  // - Rotation: OCS -> UCS?? https://help.autodesk.com/view/OARX/2020/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_MText_Rotation
  // "Accesses the angle between the X axis of the OCS for the normal vector of the current AutoCAD editor's UCS
  // and the projection of the MText object's direction vector onto the plane of the AutoCAD editor's current UCS."
  // - Direction: WCS
  // "Note that the direction vector need not be orthogonal to the normal vector." <- do not use FML
  private SOG.Plane GetTextPlane(ADB.MText target)
  {
    // Rotation prop is in UCS already: do NOT use vector converter or it will transform again!
    AG.Vector3d xDir = AG.Vector3d.XAxis.RotateBy(target.Rotation, target.Normal);
    AG.Vector3d yDir = AG.Vector3d.YAxis.RotateBy(target.Rotation, target.Normal);

    return new()
    {
      origin = _pointConverter.Convert(target.Location),
      normal = _vectorConverter.Convert(target.Normal),
      xdir = new(xDir.X, xDir.Y, xDir.Z, _settingsStore.Current.SpeckleUnits),
      ydir = new(yDir.X, yDir.Y, yDir.Z, _settingsStore.Current.SpeckleUnits),
      units = _settingsStore.Current.SpeckleUnits,
    };
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
