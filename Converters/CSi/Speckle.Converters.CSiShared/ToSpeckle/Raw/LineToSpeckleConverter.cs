using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

/// <summary>
/// Every frame has as its displayValue a Speckle line. This is defined by the start and end points.
/// </summary>
/// <remarks>
/// Display value extraction is always handled by CsiShared.
/// This is because geometry representation is the same for both Sap2000 and Etabs products.
/// TODO: Point caching
/// </remarks>
public class LineToSpeckleConverter : ITypedConverter<CsiFrameWrapper, Line>
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ITypedConverter<CsiJointWrapper, Point> _pointConverter;

  public LineToSpeckleConverter(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ITypedConverter<CsiJointWrapper, Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public Line Convert(CsiFrameWrapper target)
  {
    // TODO: Better exception handling
    string startPoint = "",
      endPoint = "";
    if (_settingsStore.Current.SapModel.FrameObj.GetPoints(target.Name, ref startPoint, ref endPoint) != 0)
    {
      throw new ArgumentException($"Failed to convert frame {target.Name}");
    }

    // TODO: Point caching. This is gross!
    double startX = 0,
      startY = 0,
      startZ = 0;
    if (_settingsStore.Current.SapModel.PointObj.GetCoordCartesian(startPoint, ref startX, ref startY, ref startZ) != 0)
    {
      throw new ArgumentException($"Failed to convert point {startPoint}");
    }

    // TODO: Point caching. This is gross!
    double endX = 0,
      endY = 0,
      endZ = 0;
    if (_settingsStore.Current.SapModel.PointObj.GetCoordCartesian(endPoint, ref endX, ref endY, ref endZ) != 0)
    {
      throw new ArgumentException($"Failed to convert point {endPoint}");
    }

    return new()
    {
      start = new Point(startX, startY, startZ, _settingsStore.Current.SpeckleUnits),
      end = new Point(endX, endY, endZ, _settingsStore.Current.SpeckleUnits),
      units = _settingsStore.Current.SpeckleUnits,
    };
  }
}
