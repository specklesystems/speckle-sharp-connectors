using Speckle.Converters.Autocad.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Annotation;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBTextToSpeckleRawConverter : ITypedConverter<ADB.DBText, Text>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBTextToSpeckleRawConverter(
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
  /// Converts AutoCAD DBText to a Speckle Text object.
  /// </summary>
  /// <param name="target">The AutoCAD DBText to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public Text Convert(ADB.DBText target) =>
    // target.WidthFactor is ignored, because we don't support 1-dimensional text scaling
    // AlignmentPoint can be ignored, as, if used for positioning, it will be already reflected in Rotation and Height
    new()
    {
      value = target.TextString,
      height = target.Height,
      maxWidth = null, // always 1 line
      plane = GetTextPlane(target),
      screenOriented = false,
      alignmentH = AlignmentHorizontal.Left, // constant relevant to Position (.Justify & .Alignment Point can be ignored)
      alignmentV = AlignmentVertical.Bottom, // constant relevant to Position (.Justify & .Alignment Point can be ignored)
      units = _settingsStore.Current.SpeckleUnits
    };

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
