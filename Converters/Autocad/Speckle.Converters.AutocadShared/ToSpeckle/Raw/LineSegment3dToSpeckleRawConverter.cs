using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class LineSegment3dToSpeckleRawConverter(
  ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<AG.LineSegment3d, SOG.Line>
{
  public Result<SOG.Line> Convert(AG.LineSegment3d target)
  {
    if (!pointConverter.Try(target.StartPoint, out var start))
    {
      return start.Failure<SOG.Line>();
    }
    if (!pointConverter.Try(target.EndPoint, out var end))
    {
      return end.Failure<SOG.Line>();
    }
    return Success(
      new SOG.Line
      {
        start = start.Value,
        end = end.Value,
        units = settingsStore.Current.SpeckleUnits,
        domain = new SOP.Interval { start = 0, end = target.Length },
      }
    );
  }
}
