using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.Raw;

public class AlignmentSubentitySpiralToSpeckleRawConverter
  : ITypedConverter<(CDB.AlignmentSubEntitySpiral, CDB.Alignment), SOG.Polyline>
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public AlignmentSubentitySpiralToSpeckleRawConverter(
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
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
        value = _referencePointConverter.ConvertDoublesToExternalCoordinates(polylineValue), // convert by ref point transform
        units = units,
        closed = spiral.StartPoint == spiral.EndPoint
      };

    // create a properties dictionary for additional props. These all can throw
    PropertyHandler propHandler = new();
    Dictionary<string, object?> props = new() { };
    propHandler.TryAddToDictionary(props, "length", () => spiral.Length);
    propHandler.TryAddToDictionary(props, "spiralDirection", () => spiral.Direction.ToString());
    propHandler.TryAddToDictionary(props, "delta", () => spiral.Delta);
    propHandler.TryAddToDictionary(props, "direction", () => spiral.SpiralDefinition.ToString());
    propHandler.TryAddToDictionary(props, "totalX", () => spiral.TotalX);
    propHandler.TryAddToDictionary(props, "totalY", () => spiral.TotalY);
    propHandler.TryAddToDictionary(props, "startStation", () => spiral.StartStation);
    propHandler.TryAddToDictionary(props, "endStation", () => spiral.EndStation);
    propHandler.TryAddToDictionary(props, "startDirection", () => spiral.StartDirection);
    propHandler.TryAddToDictionary(props, "endDirection", () => spiral.EndDirection);
    propHandler.TryAddToDictionary(props, "a", () => spiral.A);
    propHandler.TryAddToDictionary(props, "k", () => spiral.K);
    propHandler.TryAddToDictionary(props, "p", () => spiral.P);
    propHandler.TryAddToDictionary(props, "minimumTransitionLength", () => spiral.MinimumTransitionLength);
    if (props.Count > 0)
    {
      polyline["properties"] = props;
    }

    return polyline;
  }
}
