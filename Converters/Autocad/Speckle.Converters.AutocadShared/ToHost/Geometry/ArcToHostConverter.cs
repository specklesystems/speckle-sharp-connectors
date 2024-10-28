using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(nameof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Arc, ADB.Arc>
{
  private readonly ITypedConverter<SOG.Arc, AG.CircularArc3d> _arcConverter;
  private readonly ITypedConverter<SOG.Plane, AG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public ArcToHostConverter(
    ITypedConverter<SOG.Arc, AG.CircularArc3d> arcConverter,
    ITypedConverter<SOG.Plane, AG.Plane> planeConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _arcConverter = arcConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Arc)target);

  public ADB.Arc Convert(SOG.Arc target)
  {
    // the most reliable method to convert to autocad convention is to calculate from start, end, and midpoint
    // because of different plane & start/end angle conventions
    AG.CircularArc3d circularArc = _arcConverter.Convert(target);

    // calculate adjusted start and end angles from circularArc reference
    // for some reason, if just the circular arc start and end angle props are used, this moves the endpoints of the created arc
    // so we need to calculate the adjusted start and end angles from the circularArc reference vector.
    AG.Plane plane = new(circularArc.Center, circularArc.Normal);
    double angleOnPlane = circularArc.ReferenceVector.AngleOnPlane(plane);
    double adjustedStartAngle = circularArc.StartAngle + angleOnPlane;
    double adjustEndAngle = circularArc.EndAngle + angleOnPlane;

    return new(circularArc.Center, circularArc.Normal, circularArc.Radius, adjustedStartAngle, adjustEndAngle);
  }
}
