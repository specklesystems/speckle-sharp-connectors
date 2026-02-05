using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(DisplayableObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DisplayableObjectConverter
  : IToHostTopLevelConverter,
    ITypedConverter<DisplayableObject, List<(RG.GeometryBase a, Base b)>>
{
  private readonly ITypedConverter<SOG.Point, RG.Point> _pointConverter;
  private readonly ITypedConverter<SOG.Line, RG.LineCurve> _lineConverter;
  private readonly ITypedConverter<SOG.Polyline, RG.PolylineCurve> _polylineConverter;
  private readonly ITypedConverter<SOG.Arc, RG.ArcCurve> _arcConverter;
  private readonly ITypedConverter<SOG.Mesh, RG.Mesh> _meshConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public DisplayableObjectConverter(
    ITypedConverter<SOG.Point, RG.Point> pointConverter,
    ITypedConverter<SOG.Line, RG.LineCurve> lineConverter,
    ITypedConverter<SOG.Polyline, RG.PolylineCurve> polylineConverter,
    ITypedConverter<SOG.Arc, RG.ArcCurve> arcConverter,
    ITypedConverter<SOG.Mesh, RG.Mesh> meshConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _lineConverter = lineConverter;
    _polylineConverter = polylineConverter;
    _arcConverter = arcConverter;
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((DisplayableObject)target);

  public List<(RG.GeometryBase a, Base b)> Convert(DisplayableObject target)
  {
    var result = new List<RG.GeometryBase>();
    foreach (var item in target.displayValue)
    {
      RG.GeometryBase x = item switch
      {
        SOG.Line line => _lineConverter.Convert(line),
        SOG.Polyline polyline => _polylineConverter.Convert(polyline),
        SOG.Arc arc => _arcConverter.Convert(arc),
        SOG.Mesh mesh => _meshConverter.Convert(mesh),
        SOG.Point point => _pointConverter.Convert(point),
        _ => throw new ConversionException($"Found unsupported fallback geometry: {item.GetType()}"),
      };
      x.Transform(GetUnitsTransform(item));
      result.Add(x);
    }

    return result.Zip(target.displayValue, (a, b) => (a, b)).ToList();
  }

  private RG.Transform GetUnitsTransform(Base speckleObject)
  {
    /*
     * POC: CNX-9270 Looking at a simpler, more performant way of doing unit scaling on `ToNative`
     * by fully relying on the transform capabilities of the HostApp, and only transforming top-level stuff.
     * This may not hold when adding more complex conversions, but it works for now!
     */
    if (speckleObject["units"] is string units)
    {
      var scaleFactor = Units.GetConversionFactor(units, _settingsStore.Current.SpeckleUnits);
      var scale = RG.Transform.Scale(RG.Point3d.Origin, scaleFactor);
      return scale;
    }

    return RG.Transform.Identity;
  }
}
