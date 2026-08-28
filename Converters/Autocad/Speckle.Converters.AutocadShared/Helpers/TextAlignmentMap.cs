namespace Speckle.Converters.Autocad.Helpers;

/// <summary>
/// Maps an AutoCAD <see cref="ADB.AttachmentPoint"/> onto the 3x3 alignment grid Speckle's
/// <see cref="SA.Text"/> exposes. Shared by the MText converter (where it comes from
/// <c>MText.Attachment</c>) and the DBText converter (where it comes from <c>DBText.Justify</c>),
/// so both spell the same anchor semantics.
/// </summary>
public static class TextAlignmentMap
{
  /// <summary>
  /// Simplify horizontal text alignment to 3 options: Left, Center, Right.
  /// </summary>
  public static SA.AlignmentHorizontal GetHorizontalAlignment(ADB.AttachmentPoint attachmentPt) =>
    attachmentPt switch
    {
      ADB.AttachmentPoint.TopLeft
      or ADB.AttachmentPoint.MiddleLeft
      or ADB.AttachmentPoint.BottomLeft
      or ADB.AttachmentPoint.BaseLeft => SA.AlignmentHorizontal.Left,
      ADB.AttachmentPoint.TopCenter
      or ADB.AttachmentPoint.MiddleCenter
      or ADB.AttachmentPoint.BottomCenter
      or ADB.AttachmentPoint.BaseCenter
      // Middle (the DBText "MC"-like justification) is centred on both axes.
      or ADB.AttachmentPoint.BaseMid
      or ADB.AttachmentPoint.MiddleMid
      or ADB.AttachmentPoint.TopMid
      or ADB.AttachmentPoint.BottomMid => SA.AlignmentHorizontal.Center,
      ADB.AttachmentPoint.TopRight
      or ADB.AttachmentPoint.MiddleRight
      or ADB.AttachmentPoint.BottomRight
      or ADB.AttachmentPoint.BaseRight => SA.AlignmentHorizontal.Right,
      _ => SA.AlignmentHorizontal.Left,
    };

  /// <summary>
  /// Simplify vertical text alignment to 3 options: Top, Center, Bottom.
  /// </summary>
  /// <remarks>
  /// The <c>Base*</c> members are DBText baseline justifications — the baseline is the bottom of the
  /// text box for Speckle's purposes, so they map to <see cref="SA.AlignmentVertical.Bottom"/>.
  /// MText never reports them.
  /// </remarks>
  public static SA.AlignmentVertical GetVerticalAlignment(ADB.AttachmentPoint attachmentPt) =>
    attachmentPt switch
    {
      ADB.AttachmentPoint.TopLeft
      or ADB.AttachmentPoint.TopCenter
      or ADB.AttachmentPoint.TopRight
      or ADB.AttachmentPoint.TopMid
      or ADB.AttachmentPoint.TopAlign
      or ADB.AttachmentPoint.TopFit => SA.AlignmentVertical.Top,
      ADB.AttachmentPoint.MiddleLeft
      or ADB.AttachmentPoint.MiddleCenter
      or ADB.AttachmentPoint.MiddleRight
      or ADB.AttachmentPoint.MiddleAlign
      or ADB.AttachmentPoint.MiddleFit
      or ADB.AttachmentPoint.MiddleMid => SA.AlignmentVertical.Center,
      ADB.AttachmentPoint.BottomLeft
      or ADB.AttachmentPoint.BottomCenter
      or ADB.AttachmentPoint.BottomRight
      or ADB.AttachmentPoint.BottomAlign
      or ADB.AttachmentPoint.BottomFit
      or ADB.AttachmentPoint.BottomMid
      or ADB.AttachmentPoint.BaseLeft
      or ADB.AttachmentPoint.BaseCenter
      or ADB.AttachmentPoint.BaseRight
      or ADB.AttachmentPoint.BaseAlign
      or ADB.AttachmentPoint.BaseFit
      or ADB.AttachmentPoint.BaseMid => SA.AlignmentVertical.Bottom,
      _ => SA.AlignmentVertical.Top,
    };
}
