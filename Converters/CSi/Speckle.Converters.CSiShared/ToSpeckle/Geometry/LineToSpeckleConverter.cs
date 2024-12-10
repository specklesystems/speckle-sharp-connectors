using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Geometry;

/// <summary>
/// Every frame has as its displayValue a Speckle lines by defined by the endpoint coordinates.
/// </summary>
/// <remarks>
/// Creates a line from frame endpoints using the CSi API:
/// 1. Gets frame endpoint names
/// 2. Extracts coordinate values for each endpoint
/// 3. Creates a Speckle line with appropriate units
/// Throws ArgumentException if coordinate extraction fails.
/// The TODOs noted below will be completed as part of the "Data Extraction (Send)" milestone.
/// </remarks>
public class LineToSpeckleConverter : ITypedConverter<CSiFrameWrapper, Line>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;
  private readonly ITypedConverter<CSiJointWrapper, Point> _pointConverter;

  public LineToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    ITypedConverter<CSiJointWrapper, Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public Line Convert(CSiFrameWrapper target)
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
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
