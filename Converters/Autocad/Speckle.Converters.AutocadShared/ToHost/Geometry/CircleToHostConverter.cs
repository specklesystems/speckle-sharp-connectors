using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(nameof(SOG.Circle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CircleToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Circle, ADB.Circle>
{
  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly ITypedConverter<SOG.Vector, AG.Vector3d> _vectorConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public CircleToHostConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _settingsStore = settingsStore;
  }

  public HostResult Convert(Base target) => HostResult.Success(Convert((SOG.Circle)target));

  public ADB.Circle Convert(SOG.Circle target)
  {
    AG.Vector3d normal = _vectorConverter.Convert(target.plane.normal);
    AG.Point3d origin = _pointConverter.Convert(target.plane.origin);
    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);

    var radius = f * target.radius;
    return new(origin, normal, radius);
  }
}
