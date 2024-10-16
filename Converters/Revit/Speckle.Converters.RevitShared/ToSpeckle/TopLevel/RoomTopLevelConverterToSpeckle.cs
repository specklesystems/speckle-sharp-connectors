using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DBA.Room), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RoomTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DBA.Room, SOBE.Room>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ITypedConverter<DB.Level, SOBR.RevitLevel> _levelConverter;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly ITypedConverter<DB.Location, Base> _locationConverter;
  private readonly ITypedConverter<IList<DB.BoundarySegment>, SOG.Polycurve> _boundarySegmentConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public RoomTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    ITypedConverter<DB.Level, SOBR.RevitLevel> levelConverter,
    ParameterValueExtractor parameterValueExtractor,
    ITypedConverter<DB.Location, Base> locationConverter,
    ITypedConverter<IList<DB.BoundarySegment>, SOG.Polycurve> boundarySegmentConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _levelConverter = levelConverter;
    _parameterValueExtractor = parameterValueExtractor;
    _locationConverter = locationConverter;
    _boundarySegmentConverter = boundarySegmentConverter;
    _converterSettings = converterSettings;
  }

  public override SOBE.Room Convert(DBA.Room target)
  {
    var number = target.Number;
    var name = _parameterValueExtractor.GetValueAsString(target, DB.BuiltInParameter.ROOM_NAME);
    var area = _parameterValueExtractor.GetValueAsDouble(target, DB.BuiltInParameter.ROOM_AREA);

    var displayValue = _displayValueExtractor.GetDisplayValue(target);
    var basePoint = (SOG.Point)_locationConverter.Convert(target.Location);
    var level = _levelConverter.Convert(target.Level);

    var profiles = target
      .GetBoundarySegments(new DB.SpatialElementBoundaryOptions())
      .Select(c => (ICurve)_boundarySegmentConverter.Convert(c))
      .ToList();

    var outline = profiles.First();
    var voids = profiles.Skip(1).ToList();

    var speckleRoom = new SOBE.Room(name ?? "-", number, level, basePoint)
    {
      displayValue = displayValue,
      area = area,
      outline = outline,
      voids = voids,
      units = _converterSettings.Current.SpeckleUnits
    };

    // POC: Removed dynamic property `phaseCreated` as it seems the info is included in the parameters already

    return speckleRoom;
  }
}
