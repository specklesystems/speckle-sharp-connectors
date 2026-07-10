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
      Contents = target.value,
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

    if (target.maxWidth is double maxWidth)
    {
      mtext.Width = maxWidth * f;
    }
    return mtext;
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
