using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

// NOTE: This is HORRIBLE but serves just as a poc! We need point caching and weak referencing to joint objects
public class FrameToSpeckleConverter : ITypedConverter<CSiFrameWrapper, Line>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;
  private readonly ITypedConverter<CSiJointWrapper, Point> _pointConverter;

  public FrameToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    ITypedConverter<CSiJointWrapper, Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public Line Convert(CSiFrameWrapper target) // NOTE: THIS IS TEMPORARY POC
  {
    // frame points
    string startPoint = "",
      endPoint = "";
    if (_settingsStore.Current.SapModel.FrameObj.GetPoints(target.Name, ref startPoint, ref endPoint) != 0)
    {
      throw new ArgumentException($"Failed to convert frame {target.Name}");
    }

    // start point coordinates
    double startX = 0,
      startY = 0,
      startZ = 0;
    if (_settingsStore.Current.SapModel.PointObj.GetCoordCartesian(startPoint, ref startX, ref startY, ref startZ) != 0)
    {
      throw new ArgumentException($"Failed to convert point {startPoint}");
    }

    // end point coordinates
    double endX = 0,
      endY = 0,
      endZ = 0;
    if (_settingsStore.Current.SapModel.PointObj.GetCoordCartesian(endPoint, ref endX, ref endY, ref endZ) != 0)
    {
      throw new ArgumentException($"Failed to convert point {endPoint}");
    }

    // Create and return the line
    return new()
    {
      start = new Point(startX, startY, startZ, _settingsStore.Current.SpeckleUnits),
      end = new Point(endX, endY, endZ, _settingsStore.Current.SpeckleUnits),
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
