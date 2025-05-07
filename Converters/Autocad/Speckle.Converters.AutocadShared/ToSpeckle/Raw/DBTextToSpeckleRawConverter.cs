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
    // target.WidthFactor is ignored, because we don't support 1-dimensional text scaling
    // AlignmentPoint can be ignored, as, if used for positioning, it will be already reflected in Rotation and Height
    new()
    {
      value = target.TextString,
      height = target.Height,
      maxWidth = null, // always 1 line
      origin = _pointConverter.Convert(target.Position),
      plane = GetTextPlane(target),
      alignmentH = SA.AlignmentHorizontal.Left, // constant relevant to Position (.Justify & .Alignment Point can be ignored)
      alignmentV = SA.AlignmentVertical.Bottom, // constant relevant to Position (.Justify & .Alignment Point can be ignored)
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
}
