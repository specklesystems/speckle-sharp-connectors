using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

public class FrameToSpeckleConverter : ITypedConverter<CSiFrameWrapper, Line>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;
  private readonly ITypedConverter<CSiJointWrapper, Point> _pointConverter;
  private readonly ICSiApplicationService _csiApplicationService; // I need access to the SapModel here, but this is in the connectors project.

  public FrameToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    ITypedConverter<CSiJointWrapper, Point> pointConverter,
    ICSiApplicationService csiApplicationService
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _csiApplicationService = csiApplicationService;
  }

  public Line Convert(CSiFrameWrapper target)
  {
    string Point1 = "";
    string Point2 = "";
    int result = cSapModel.FrameObj.GetPoints(target.Name, ref Point1, ref Point2);
    if (result != 0)
    {
      throw new Exception($"Failed to convert frame {target.Name}");
    }

    return new()
    {
      start = _pointConverter.Convert(Point1),
      end = _pointConverter.Convert(Point2),
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
