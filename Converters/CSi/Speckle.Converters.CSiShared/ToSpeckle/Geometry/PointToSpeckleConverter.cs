using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Geometry;

/// <summary>
/// Every joint has as its displayValue a Speckle point defined by extracting their coordinates.
/// </summary>
/// <remarks>
/// Creates a point from joint coordinates using the CSi API:
/// 1. Extracts cartesian coordinates
/// 2. Creates a Speckle point with appropriate units
///
/// TODO: Current implementation is a proof of concept, needs refinement
/// The TODOs noted will be completed as part of the "Data Extraction (Send)" milestone.
///
/// Throws ArgumentException if coordinate extraction fails.
/// </remarks>
public class PointToSpeckleConverter : ITypedConverter<CSiJointWrapper, Point>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingStore;

  public PointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingStore)
  {
    _settingStore = settingStore;
  }

  public Point Convert(CSiJointWrapper target) // NOTE: This is just a temporary POC
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
