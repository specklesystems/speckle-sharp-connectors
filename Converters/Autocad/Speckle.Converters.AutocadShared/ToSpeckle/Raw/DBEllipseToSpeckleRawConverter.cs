using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBEllipseToSpeckleRawConverter : ITypedConverter<ADB.Ellipse, SOG.Ellipse>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBEllipseToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Vector3d, SOG.Vector> vectorConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Ellipse)target);

  public SOG.Ellipse Convert(ADB.Ellipse target)
  {
    SOG.Plane plane =
      new()
      {
        origin = _pointConverter.Convert(target.Center),
        normal = _vectorConverter.Convert(target.Normal),
        xdir = _vectorConverter.Convert(target.MajorAxis),
        ydir = _vectorConverter.Convert(target.MinorAxis),
        units = _settingsStore.Current.SpeckleUnits
      };

    // the start and end param corresponds to start and end angle in radians
    SOP.Interval trim = new() { start = target.StartAngle, end = target.EndAngle };

    SOG.Ellipse ellipse =
      new()
      {
        plane = plane,
        firstRadius = target.MajorRadius,
        secondRadius = target.MinorRadius,
        domain = new SOP.Interval { start = 0, end = Math.PI * 2 },
        trimDomain = trim,
        length = target.GetDistanceAtParameter(target.EndParam),
        units = _settingsStore.Current.SpeckleUnits
      };

    return ellipse;
  }
}
