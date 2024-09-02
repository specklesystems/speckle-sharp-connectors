using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class PointToHostConverter : ITypedConverter<SOG.Point, ACG.MapPoint>
{
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PointToHostConverter(IConverterSettingsStore<ArcGISConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Point)target);

  public ACG.MapPoint Convert(SOG.Point target)
  {
    SOG.Point scaledMovedRotatedPoint = _settingsStore.Current.ActiveCRSoffsetRotation.OffsetRotateOnReceive(target);
    return new ACG.MapPointBuilderEx(
      scaledMovedRotatedPoint.x,
      scaledMovedRotatedPoint.y,
      scaledMovedRotatedPoint.z,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
