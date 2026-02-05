using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class ArcToSpeckleConverter : ITypedConverter<DB.Arc, SOG.Arc>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<
    (DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal),
    SOG.Plane
  > _curveOriginToPlaneConverter;

  public ArcToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<(DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal), SOG.Plane> curveOriginToPlaneConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _curveOriginToPlaneConverter = curveOriginToPlaneConverter;
  }

  public SOG.Arc Convert(DB.Arc target)
  {
    // Revit arcs are always counterclockwise in the arc normal direction. This aligns with Speckle arc plane convention.
    DB.XYZ start = target.Evaluate(0, true);
    DB.XYZ end = target.Evaluate(1, true);
    DB.XYZ mid = target.Evaluate(0.5, true);

    return new SOG.Arc()
    {
      plane = _curveOriginToPlaneConverter.Convert(
        (target.Center, target.XDirection, target.YDirection, target.Normal)
      ),
      units = _converterSettings.Current.SpeckleUnits,
      endPoint = _xyzToPointConverter.Convert(end),
      startPoint = _xyzToPointConverter.Convert(start),
      midPoint = _xyzToPointConverter.Convert(mid),
      domain = new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) }
    };
  }
}
