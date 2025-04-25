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

    // Civil 3D 2022 has a bug with the spiral definition sometimes throwing an InvalidOperation exception
    // Catch the error here and set direction to null if this occurs
    string? spiralDirection;
    try
    {
      spiralDirection = spiral.Direction.ToString();
    }
    catch (InvalidOperationException)
    {
      // Set the spiralDirection as null
      spiralDirection = null;
    }

    SOG.Polyline polyline =
      new()
      {
        value = polylineValue,
        units = units,
        closed = spiral.StartPoint == spiral.EndPoint,
        // add alignment spiral props
        length = TryGetValue(() => spiral.Length),
        ["delta"] = TryGetValue(() => spiral.Delta),
        ["direction"] = TryGetValue(() => spiral.Direction.ToString()),
        ["spiralDefinition"] = TryGetValue(() => spiral.SpiralDefinition.ToString()),
        ["totalX"] = TryGetValue(() => spiral.TotalX),
        ["totalY"] = TryGetValue(() => spiral.TotalY),
        ["startStation"] = TryGetValue(() => spiral.StartStation),
        ["endStation"] = TryGetValue(() => spiral.EndStation),
        ["startDirection"] = TryGetValue(() => spiral.StartDirection),
        ["endDirection"] = TryGetValue(() => spiral.EndDirection),
        ["a"] = TryGetValue(() => spiral.A),
        ["k"] = TryGetValue(() => spiral.K),
        ["p"] = TryGetValue(() => spiral.P),
        ["minimumTransitionLength"] = TryGetValue(() => spiral.MinimumTransitionLength)
      };

    return polyline;
  }
  
  private T? TryGetValue<T>(Func<T> getValue)
  {
    try
    {
      return getValue();
    }
    catch (InvalidOperationException)
    {
      return default;
    }
  }
}
