using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.AutocadShared.ToHost.Raw;

/// <summary>
/// Polycurve segments might appear in different ICurve types which requires to handle separately for each segment.
/// </summary>
public class PolycurveToHostSplineRawConverter(
  ITypedConverter<SOG.Line, ADB.Line> lineConverter,
  ITypedConverter<SOG.Polyline, ADB.Polyline3d> polylineConverter,
  ITypedConverter<SOG.Arc, ADB.Arc> arcConverter,
  ITypedConverter<SOG.Curve, ADB.Curve> curveConverter
) : ITypedConverter<SOG.Polycurve, List<Result<ADB.Entity>>>
{
  public Result<List<Result<ADB.Entity>>> Convert(SOG.Polycurve target)
  {
    // POC: We can improve this once we have IIndex of raw converters and we can get rid of case converters?
    // POC: Should we join entities?
    var list = new List<Result<ADB.Entity>>();

    foreach (var segment in target.segments)
    {
      switch (segment)
      {
        case SOG.Arc arc:
          list.Add(arcConverter.Convert(arc).To<ADB.Arc, ADB.Entity>());
          break;
        case SOG.Line line:
          list.Add(lineConverter.Convert(line).To<ADB.Line, ADB.Entity>());
          break;
        case SOG.Polyline polyline:
          list.Add(polylineConverter.Convert(polyline).To<ADB.Polyline3d, ADB.Entity>());
          break;
        case SOG.Curve curve:
          list.Add(curveConverter.Convert(curve).To<ADB.Curve, ADB.Entity>());
          break;
        default:
          break;
      }
    }

    return Success(list);
  }
}
