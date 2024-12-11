using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

/// <summary>
/// Every joint has as its displayValue a Speckle point. This is defined by extracting their coordinates.
/// </summary>
/// <remarks>
/// Display value extraction is always handled by CsiShared.
/// This is because geometry representation is the same for both Sap2000 and Etabs products.
/// </remarks>
public class PointToSpeckleConverter : ITypedConverter<CsiJointWrapper, Point>
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingStore;

  public PointToSpeckleConverter(IConverterSettingsStore<CsiConversionSettings> settingStore)
  {
    _settingStore = settingStore;
  }

  public Point Convert(CsiJointWrapper target) // NOTE: This is just a temporary POC
  {
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

    return new(pointX, pointY, pointZ, _settingStore.Current.SpeckleUnits);
  }
}
