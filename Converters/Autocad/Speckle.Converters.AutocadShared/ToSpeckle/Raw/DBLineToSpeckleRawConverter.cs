using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBLineToSpeckleRawConverter(
  ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
  ITypedConverter<ADB.Extents3d, SOG.Box> boxConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<ADB.Line, SOG.Line>
{
  public Result<SOG.Line> Convert(ADB.Line target) =>
    Success<SOG.Line>(
      new()
      {
        start = pointConverter.Convert(target.StartPoint).Value,
        end = pointConverter.Convert(target.EndPoint).Value,
        units = settingsStore.Current.SpeckleUnits,
        domain = new SOP.Interval { start = 0, end = target.Length },
        bbox = boxConverter.Convert(target.GeometricExtents).Value
      }
    );
}
