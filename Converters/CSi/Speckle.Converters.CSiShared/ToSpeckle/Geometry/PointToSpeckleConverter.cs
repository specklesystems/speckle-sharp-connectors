using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Geometry;

// NOTE: This is HORRIBLE but serves just as a poc!
public class PointToSpeckleConverter : ITypedConverter<CSiJointWrapper, Point>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingStore;

  public PointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingStore)
  {
    _settingStore = settingStore;
  }

  public Point Convert(CSiJointWrapper target) // NOTE: This is just a temporary POC
  {
    string applicationId = "";

    _ = _settingStore.Current.SapModel.PointObj.GetGUID(target.Name, ref applicationId);

    double pointX = 0;
    double pointY = 0;
    double pointZ = 0;

    int result = _settingStore.Current.SapModel.PointObj.GetCoordCartesian(
      target.Name,
      ref pointX,
      ref pointY,
      ref pointZ
    );

    if (result != 0)
    {
      throw new ArgumentException($"Failed to convert {target.Name} to {typeof(Point)}");
    }

    return new(pointX, pointY, pointZ, _settingStore.Current.SpeckleUnits, applicationId);
  }
}
