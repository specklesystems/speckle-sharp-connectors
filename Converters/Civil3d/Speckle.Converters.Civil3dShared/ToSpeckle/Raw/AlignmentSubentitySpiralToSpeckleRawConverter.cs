using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class AlignmentSubentitySpiralToSpeckleRawConverter
  : ITypedConverter<(CDB.AlignmentSubEntitySpiral, CDB.Alignment), SOG.Polyline>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public AlignmentSubentitySpiralToSpeckleRawConverter(
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Polyline Convert(object target) => Convert(((CDB.AlignmentSubEntitySpiral, CDB.Alignment))target);

  public SOG.Polyline Convert((CDB.AlignmentSubEntitySpiral, CDB.Alignment) target)
  {
    CDB.AlignmentSubEntitySpiral spiral = target.Item1;
    CDB.Alignment alignment = target.Item2;

    string units = _settingsStore.Current.SpeckleUnits;

    // create polyline, default tessellation length is 1
    var tessellation = 1;
    int spiralSegmentCount = System.Convert.ToInt32(Math.Ceiling(spiral.Length / tessellation));
    spiralSegmentCount = (spiralSegmentCount < 10) ? 10 : spiralSegmentCount;
    double spiralSegmentLength = spiral.Length / spiralSegmentCount;
    List<double> polylineValue = new(spiralSegmentCount * 3) { spiral.StartPoint.X, spiral.StartPoint.Y, 0 };
    for (int i = 1; i < spiralSegmentCount; i++)
    {
      double x = 0;
      double y = 0;
      alignment.PointLocation(spiral.StartStation + i * spiralSegmentLength, 0, ref x, ref y);
      polylineValue.Add(x);
      polylineValue.Add(y);
      polylineValue.Add(0);
    }
    polylineValue.Add(spiral.EndPoint.X);
    polylineValue.Add(spiral.EndPoint.Y);
    polylineValue.Add(0);

    SOG.Polyline polyline =
      new()
      {
        value = polylineValue,
        units = units,
        closed = spiral.StartPoint == spiral.EndPoint,
        // add alignment spiral props
        length = spiral.Length,
        ["delta"] = spiral.Delta,
        ["direction"] = spiral.Direction.ToString(),
        ["spiralDefinition"] = spiral.SpiralDefinition.ToString(),
        ["totalX"] = spiral.TotalX,
        ["totalY"] = spiral.TotalY,
        ["startStation"] = spiral.StartStation,
        ["endStation"] = spiral.EndStation,
        ["startDirection"] = spiral.StartDirection,
        ["endDirection"] = spiral.EndDirection,
        ["a"] = spiral.A,
        ["k"] = spiral.K,
        ["p"] = spiral.P,
        ["minimumTransitionLength"] = spiral.MinimumTransitionLength
      };

    return polyline;
  }
}
