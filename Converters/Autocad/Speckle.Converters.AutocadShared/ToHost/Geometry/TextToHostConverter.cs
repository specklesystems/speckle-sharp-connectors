using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(SA.Text), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextToHostConverter : IToHostTopLevelConverter, ITypedConverter<SA.Text, ADB.MText>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public TextToHostConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SA.Text)target);

  /// <remarks>⚠️ This conversion DOES perform scaling: entity transforms do not scale MText height/width.</remarks>
  public ADB.MText Convert(SA.Text target)
  {
    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    var mtext = new ADB.MText
    {
      Contents = ToMTextContents(target.value),
      TextHeight = target.height * f,
      Attachment = GetAttachment(target.alignmentH, target.alignmentV),
    };

    // orientation before location: the normal/direction setters re-plane the entity.
    var normal = new AG.Vector3d(target.plane.normal.x, target.plane.normal.y, target.plane.normal.z);
    if (!normal.IsZeroLength())
    {
      mtext.Normal = normal.GetNormal();
    }
    var xdir = new AG.Vector3d(target.plane.xdir.x, target.plane.xdir.y, target.plane.xdir.z);
    if (!xdir.IsZeroLength())
    {
      mtext.Direction = xdir.GetNormal();
    }
    mtext.Location = _pointConverter.Convert(target.plane.origin);

    // Only a positive wrap width is a wrap width: MText.Width == 0 IS AutoCAD's "no wrap", and versions before
    // [ENG-8827] published that 0 verbatim, so treat a non-positive maxWidth as "unset" rather than a zero column.
    if (target.maxWidth is double maxWidth && maxWidth > 0)
    {
      mtext.Width = maxWidth * f;
    }
    return mtext;
  }

  /// <summary>
  /// Turns the plain text Speckle carries into MText contents. MText contents is a mini markup language:
  /// <c>\</c>, <c>{</c> and <c>}</c> are control characters that must be escaped or AutoCAD swallows them as
  /// formatting, and a paragraph break is <c>\P</c>, not a newline character.
  /// </summary>
  private static string ToMTextContents(string value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return value;
    }

    // Escape the control characters FIRST (so the \P inserted below isn't escaped in turn), then fold
    // CRLF/CR/LF down to the single paragraph-break code.
    string result = value.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
    return result.Replace("\r\n", "\\P").Replace("\r", "\\P").Replace("\n", "\\P");
  }

  private static ADB.AttachmentPoint GetAttachment(SA.AlignmentHorizontal h, SA.AlignmentVertical v) =>
    (v, h) switch
    {
      (SA.AlignmentVertical.Top, SA.AlignmentHorizontal.Left) => ADB.AttachmentPoint.TopLeft,
      (SA.AlignmentVertical.Top, SA.AlignmentHorizontal.Center) => ADB.AttachmentPoint.TopCenter,
      (SA.AlignmentVertical.Top, SA.AlignmentHorizontal.Right) => ADB.AttachmentPoint.TopRight,
      (SA.AlignmentVertical.Center, SA.AlignmentHorizontal.Left) => ADB.AttachmentPoint.MiddleLeft,
      (SA.AlignmentVertical.Center, SA.AlignmentHorizontal.Center) => ADB.AttachmentPoint.MiddleCenter,
      (SA.AlignmentVertical.Center, SA.AlignmentHorizontal.Right) => ADB.AttachmentPoint.MiddleRight,
      (SA.AlignmentVertical.Bottom, SA.AlignmentHorizontal.Left) => ADB.AttachmentPoint.BottomLeft,
      (SA.AlignmentVertical.Bottom, SA.AlignmentHorizontal.Center) => ADB.AttachmentPoint.BottomCenter,
      (SA.AlignmentVertical.Bottom, SA.AlignmentHorizontal.Right) => ADB.AttachmentPoint.BottomRight,
      _ => ADB.AttachmentPoint.TopLeft,
    };
}
