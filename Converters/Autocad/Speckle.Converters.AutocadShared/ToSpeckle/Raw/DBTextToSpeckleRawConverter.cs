using Speckle.Converters.Autocad.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Annotation;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBTextToSpeckleRawConverter : ITypedConverter<ADB.DBText, Text>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly ITypedConverter<ADB.MText, Text> _mtextConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBTextToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
    ITypedConverter<ADB.MText, Text> mtextConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _mtextConverter = mtextConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts AutoCAD DBText to a Speckle Text object.
  /// </summary>
  /// <param name="target">The AutoCAD DBText to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public Text Convert(ADB.DBText target)
  {
    // Multi-line attributes are backed by an MText. Convert via the MText converter so the
    // viewer keeps the wrap and renders multiple lines.
    if (TryGetBackingMText(target, out ADB.MText? mtext))
    {
      using (mtext)
      {
        return _mtextConverter.Convert(mtext);
      }
    }

    // target.WidthFactor is ignored, because we don't support 1-dimensional text scaling
    // AlignmentPoint can be ignored, as, if used for positioning, it will be already reflected in Rotation and Height
    return new()
    {
      value = target.TextString ?? string.Empty,
      height = target.Height,
      maxWidth = null, // always 1 line
      plane = GetTextPlane(target),
      screenOriented = false,
      alignmentH = AlignmentHorizontal.Left, // constant relevant to Position (.Justify & .Alignment Point can be ignored)
      alignmentV = AlignmentVertical.Bottom, // constant relevant to Position (.Justify & .Alignment Point can be ignored)
      units = _settingsStore.Current.SpeckleUnits,
    };
  }

  private static bool TryGetBackingMText(ADB.DBText target, out ADB.MText? mtext)
  {
    switch (target)
    {
      case ADB.AttributeReference attRef when attRef.IsMTextAttribute:
        mtext = attRef.MTextAttribute;
        return true;
      case ADB.AttributeDefinition attDef when attDef.IsMTextAttributeDefinition:
        mtext = attDef.MTextAttributeDefinition;
        return true;
      default:
        mtext = null;
        return false;
    }
  }

  // For DBText, the following properties are stored in:
  // - Position: WCS
  // - Normal: WCS
  // - Rotation: OCS -> WCS https://help.autodesk.com/view/OARX/2020/ENU/?guid=OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DBText_Rotation
  private SOG.Plane GetTextPlane(ADB.DBText target)
  {
    // Rotation prop is in OCS: calculate the x and y axis based in WCS
    AG.Matrix3d transform = TransformHelper.GetTransformFromOCSToWCS(target.Normal).Inverse();
    AG.Vector3d xDir = AG.Vector3d.XAxis.RotateBy(target.Rotation, target.Normal).TransformBy(transform);
    AG.Vector3d yDir = AG.Vector3d.YAxis.RotateBy(target.Rotation, target.Normal).TransformBy(transform);

    return new()
    {
      origin = _pointConverter.Convert(target.Position),
      normal = _vectorConverter.Convert(target.Normal),
      xdir = _vectorConverter.Convert(xDir),
      ydir = _vectorConverter.Convert(yDir),
      units = _settingsStore.Current.SpeckleUnits,
    };
  }
}
